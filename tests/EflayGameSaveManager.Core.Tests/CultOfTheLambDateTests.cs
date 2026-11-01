using System.IO.Compression;
using System.Text;
using System.Text.Json;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services;
using EflayGameSaveManager.Core.Services.GamePlugins;

namespace EflayGameSaveManager.Core.Tests;

public sealed class CultOfTheLambDateTests
{
    private static readonly DateTime SavedAt = new(2026, 10, 23, 15, 32, 22, DateTimeKind.Utc);
    private readonly CultOfTheLambSavePlugin _plugin = new();

    [Theory]
    [InlineData("{\"Day\":19}")]
    [InlineData("{\"Day\":19,\"SaveDate\":\"0001-01-01T00:00:00\"}")]
    [InlineData("{\"Day\":19,\"SaveDate\":null}")]
    [InlineData("{\"Day\":19,\"SaveDate\":\"invalid\"}")]
    public void MissingOrDefaultDate_UsesSourceTime_AndIsIdempotent(string json)
    {
        var prepared = _plugin.PrepareCanonicalForTransfer("meta_0", Encoding.UTF8.GetBytes(json), SavedAt);
        using var document = JsonDocument.Parse(prepared);
        var date = document.RootElement.GetProperty("SaveDate");
        Assert.Equal(SavedAt, date.GetDateTime().ToUniversalTime());
        Assert.Equal(SavedAt.ToLocalTime(), date.GetDateTime());
        Assert.Equal(new DateTimeOffset(SavedAt).ToLocalTime().Offset, date.GetDateTimeOffset().Offset);
        Assert.Equal(19, document.RootElement.GetProperty("Day").GetInt32());
        Assert.Same(prepared, _plugin.PrepareCanonicalForTransfer("meta_0", prepared, SavedAt.AddDays(1)));
    }

    [Theory]
    [InlineData("meta_0", "{\"Day\":19,\"SaveDate\":\"2025-02-03T04:05:06+08:00\"}")]
    [InlineData("slot_0", "{\"CurrentDayIndex\":19}")]
    [InlineData("settings", "{\"Day\":19}")]
    public void ValidDatesAndGameplayData_ArePreservedByteForByte(string name, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        Assert.Same(bytes, _plugin.PrepareCanonicalForTransfer(name, bytes, SavedAt));
    }

    [Theory]
    [InlineData(GameSaveTargetPlatform.SteamPc)]
    [InlineData(GameSaveTargetPlatform.AndroidPort)]
    public void BackupAndRestore_PreserveOriginalDateAcrossBothPlatforms(GameSaveTargetPlatform source)
    {
        var root = Path.Combine(Path.GetTempPath(), "cotl-date-" + Guid.NewGuid().ToString("N"));
        var input = Path.Combine(root, "source");
        var output = Path.Combine(root, "target");
        Directory.CreateDirectory(input);
        try
        {
            var file = Path.Combine(input, _plugin.ToTargetName("meta_0", source));
            File.WriteAllBytes(file, _plugin.FromCanonical(Encoding.UTF8.GetBytes("{\"Day\":19}"), source));
            File.SetLastWriteTimeUtc(file, SavedAt);
            var device = new CurrentDeviceContext("device", "PC", false, AppPlatform.Windows);
            var game = Snapshot(input);
            var service = new ArchiveTransferService();
            var archive = service.CreateCurrentDeviceArchive(game, device, Path.Combine(root, "backup"));
            Assert.Equal(SavedAt, File.GetLastWriteTimeUtc(file));
            var target = source == GameSaveTargetPlatform.SteamPc ? GameSaveTargetPlatform.AndroidPort : GameSaveTargetPlatform.SteamPc;
            service.RestoreCurrentDeviceArchive(archive, Snapshot(output), device, target);
            AssertDate(Path.Combine(output, _plugin.ToTargetName("meta_0", target)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyArchive_RestoresDateFromEntryTime_EvenWhenAlreadyAndroidEncoded(bool encoded)
    {
        var root = Path.Combine(Path.GetTempPath(), "cotl-old-date-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "old.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("0/content/meta_0");
                // ZIP stores local wall time without an offset.
                entry.LastWriteTime = new DateTimeOffset(SavedAt.ToLocalTime());
                using var stream = entry.Open();
                var json = Encoding.UTF8.GetBytes("{\"Day\":19}");
                stream.Write(encoded ? _plugin.FromCanonical(json, GameSaveTargetPlatform.AndroidPort) : json);
            }
            var output = Path.Combine(root, "target");
            new ArchiveTransferService().RestoreCurrentDeviceArchive(archive, Snapshot(output),
                new CurrentDeviceContext("device", "Android", false, AppPlatform.Android), GameSaveTargetPlatform.AndroidPort);
            AssertDate(Path.Combine(output, "meta_0"));
            Assert.False(Directory.Exists(Path.Combine(output, "content")));
        }
        finally { Directory.Delete(root, true); }
    }

    private void AssertDate(string file)
    {
        using var json = JsonDocument.Parse(CultOfTheLambSavePlugin.ToCanonicalBytes(Path.GetFileName(file), File.ReadAllBytes(file)));
        Assert.Equal(SavedAt, json.RootElement.GetProperty("SaveDate").GetDateTime().ToUniversalTime());
        Assert.Equal(19, json.RootElement.GetProperty("Day").GetInt32());
        Assert.Equal(SavedAt, File.GetLastWriteTimeUtc(file));
    }

    private static GameSnapshot Snapshot(string path) => new("咩咩启示录", true,
        [new ResolvedSaveUnit(0, SaveUnitType.Folder, false, [new ResolvedDevicePath("device", "PC", path)], ContentsOnly: true)],
        [], [new CurrentDevicePathInfo(0, SaveUnitType.Folder, path, false)], new CurrentDeviceGamePathInfo(string.Empty), "咩咩启示录");
}
