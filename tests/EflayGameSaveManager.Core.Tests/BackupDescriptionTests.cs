using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services;

namespace EflayGameSaveManager.Core.Tests;

public sealed class BackupDescriptionTests
{
    private const string Date = "2026-10-26_10-00-00";
    private const string Manifest = """
        {"name":"Game","backups":[
          {"date":"2026-10-26_10-00-00","describe":"PC original","path":"pc.zip","size":123,"parent":"older","device_id":"pc"},
          {"date":"2026-10-26_10-00-00","describe":"Android original","path":"android.zip","size":456,"parent":null,"device_id":"android"},
          {"date":"older","describe":"Keep this","path":"older.zip","size":78,"parent":null,"device_id":"pc"}
        ],"device_heads":{"pc":"2026-10-26_10-00-00","android":"2026-10-26_10-00-00"},"sync_version":7}
        """;

    [Theory]
    [InlineData(false, "第17天\n击败 Boss，准备下一关")]
    [InlineData(true, "第17天\n击败 Boss，准备下一关")]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(false, "  新描述  ")]
    [InlineData(true, "  新描述  ")]
    public async Task Edit_OnlyChangesSelectedDevicesDescription_AndPersistsOnReload(bool cloud, string description)
    {
        using var fixture = new Fixture();
        await fixture.UpdateAsync(cloud, "PC", description);
        var actual = JsonNode.Parse(fixture.ReadManifest(cloud))!;
        var expected = JsonNode.Parse(Manifest)!;
        expected["backups"]![0]!["describe"] = description.Trim();
        // The existing manifest serializer omits null parents.
        foreach (var entry in expected["backups"]!.AsArray())
            if (entry!["parent"] is null) entry.AsObject().Remove("parent");
        Assert.True(JsonNode.DeepEquals(expected, actual));
        Assert.Equal("untouched zip bytes", File.ReadAllText(fixture.ArchivePath));

        var device = new CurrentDeviceContext("pc", "PC", false, "windows");
        var descriptions = cloud
            ? (await fixture.Cloud.ListGameBackupsAsync(fixture.Game, device, fixture.Settings)).Select(item => item.Describe)
            : (await fixture.Local.ListBackupsAsync("Game", fixture.Root, device)).Select(item => item.Describe);
        Assert.Contains(description.Trim(), descriptions);
        if (cloud)
        {
            Assert.Single(fixture.Handler.Writes);
            Assert.EndsWith("/Backups.json", fixture.Handler.Writes[0]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongDevice_DoesNotFallBackToAnotherBackupOrWriteAnything(bool cloud)
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.UpdateAsync(cloud, "missing", "wrong"));
        Assert.Equal(Manifest, fixture.ReadManifest(cloud));
        Assert.Empty(fixture.Handler.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateIdentity_IsRejectedWithoutWriting(bool cloud)
    {
        using var fixture = new Fixture(Manifest.Replace("\"device_id\":\"android\"", "\"device_id\":\"pc\""));
        var before = fixture.ReadManifest(cloud);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.UpdateAsync(cloud, "pc", "ambiguous"));
        Assert.Equal(before, fixture.ReadManifest(cloud));
        Assert.Empty(fixture.Handler.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyEmptyDeviceId_IsMatchedExactly(bool cloud)
    {
        using var fixture = new Fixture(Manifest.Replace("\"device_id\":\"android\"", "\"device_id\":\"\""));
        await fixture.UpdateAsync(cloud, "", "legacy save");
        var actual = JsonNode.Parse(fixture.ReadManifest(cloud))!;
        Assert.Equal("PC original", actual["backups"]![0]!["describe"]!.GetValue<string>());
        Assert.Equal("legacy save", actual["backups"]![1]!["describe"]!.GetValue<string>());
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "BackupDescriptionTests", Guid.NewGuid().ToString("N"));
        public string ArchivePath => Path.Combine(Root, "Game", "pc.zip");
        private string ManifestPath => Path.Combine(Root, "Game", "Backups.json");
        public LocalSaveService Local { get; } = new();
        public StorageHandler Handler { get; }
        private readonly HttpClient _http;
        public CloudSyncService Cloud { get; }
        public GameSnapshot Game { get; } = new("Game", true, [], [], [], new CurrentDeviceGamePathInfo(string.Empty));
        public CloudSettings Settings { get; } = new()
        {
            RootPath = "test",
            Backend = new CloudBackendSettings
            {
                Type = "S3", Endpoint = "https://storage.example.invalid", Bucket = "bucket",
                Region = "us-east-1", AccessKeyId = "test-access", SecretAccessKey = "test-secret"
            }
        };

        public Fixture(string manifest = Manifest)
        {
            Directory.CreateDirectory(Path.Combine(Root, "Game"));
            File.WriteAllText(ManifestPath, manifest);
            File.WriteAllText(ArchivePath, "untouched zip bytes");
            Handler = new StorageHandler(manifest);
            _http = new HttpClient(Handler);
            Cloud = new CloudSyncService(new S3CompatibleCloudStorageClient(_http), new SaveBackupService(), new ArchiveTransferService());
        }

        public Task UpdateAsync(bool cloud, string deviceId, string description) => cloud
            ? Cloud.UpdateGameBackupDescriptionAsync(Game, Settings, Date, deviceId, description)
            : Local.UpdateBackupDescriptionAsync("Game", Root, Date, deviceId, description);

        public string ReadManifest(bool cloud) => cloud ? Handler.Json : File.ReadAllText(ManifestPath);

        public void Dispose()
        {
            _http.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class StorageHandler(string json) : HttpMessageHandler
    {
        public string Json { get; private set; } = json;
        public List<string> Writes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Assert.EndsWith("/Backups.json", path); // Editing must never read/write an archive or sync_state.json.
            if (request.Method == HttpMethod.Put)
            {
                Json = await request.Content!.ReadAsStringAsync(cancellationToken);
                Writes.Add(path);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json, Encoding.UTF8, "application/json") };
        }
    }
}
