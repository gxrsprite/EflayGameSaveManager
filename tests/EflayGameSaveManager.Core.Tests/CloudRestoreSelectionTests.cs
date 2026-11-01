using System.IO.Compression;
using System.Net;
using System.Text;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services;

namespace EflayGameSaveManager.Core.Tests;

public sealed class CloudRestoreSelectionTests
{
    [Theory]
    [InlineData(false, false, "day-17")]
    [InlineData(true, false, "day-17")]
    [InlineData(false, true, "day-16")]
    public async Task Restore_UsesLatestAcrossDevicesUnlessAnOlderBackupIsExplicitlySelected(
        bool compatibilityCurrentMethod, bool selectOlderBackup, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "CloudRestoreSelectionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var handler = new StorageHandler();
            using var http = new HttpClient(handler);
            var service = new CloudSyncService(new S3CompatibleCloudStorageClient(http), new SaveBackupService(), new ArchiveTransferService());
            var target = Path.Combine(root, "saves");
            var game = new GameSnapshot("Game", true,
                [new ResolvedSaveUnit(0, SaveUnitType.Folder, false,
                    [new ResolvedDevicePath("pc", "PC", target)], ContentsOnly: true)],
                [], [], new CurrentDeviceGamePathInfo(string.Empty));
            var device = new CurrentDeviceContext("pc", "PC", false, "windows");
            var settings = new CloudSettings
            {
                RootPath = "test",
                Backend = new CloudBackendSettings
                {
                    Type = "S3", Endpoint = "https://storage.example.invalid", Bucket = "bucket",
                    Region = "us-east-1", AccessKeyId = "test-access", SecretAccessKey = "test-secret"
                }
            };

            if (selectOlderBackup)
                await service.RestoreGameBackupAsync(game, device, settings, "2026-10-23_12-00-00", "pc");
            else if (compatibilityCurrentMethod)
                await service.RestoreGameCurrentSaveAsync(game, device, settings);
            else
                await service.RestoreGameLatestSaveAsync(game, device, settings);

            Assert.Equal(expected, File.ReadAllText(Path.Combine(target, "progress.txt")));
            Assert.Single(handler.DownloadedArchives);
            Assert.EndsWith(selectOlderBackup ? "2026-10-23_12-00-00.zip" : "2026-10-24_13-00-00.zip", handler.DownloadedArchives[0]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class StorageHandler : HttpMessageHandler
    {
        public List<string> DownloadedArchives { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            if (path.EndsWith("/Backups.json", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {"name":"Game","backups":[
                          {"date":"2026-10-23_12-00-00","describe":"PC day 16","path":"save_data/Game/2026-10-23_12-00-00.zip","size":100,"device_id":"pc"},
                          {"date":"2026-10-24_13-00-00","describe":"Android day 17","path":"save_data/Game/2026-10-24_13-00-00.zip","size":100,"device_id":"android"}
                        ],"device_heads":{"pc":"2026-10-23_12-00-00","android":"2026-10-24_13-00-00"},"sync_version":0}
                        """, Encoding.UTF8, "application/json")
                });
            if (path.EndsWith(".zip", StringComparison.Ordinal))
            {
                DownloadedArchives.Add(path);
                using var memory = new MemoryStream();
                using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
                using (var writer = new StreamWriter(archive.CreateEntry("0/content/progress.txt").Open()))
                    writer.Write(path.EndsWith("2026-10-24_13-00-00.zip", StringComparison.Ordinal) ? "day-17" : "day-16");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(memory.ToArray()) });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
