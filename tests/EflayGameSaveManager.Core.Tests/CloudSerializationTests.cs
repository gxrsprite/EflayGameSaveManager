using System.Text.Encodings.Web;
using System.Text.Json;
using EflayGameSaveManager.Core.Models;

namespace EflayGameSaveManager.Core.Tests;

public sealed class CloudSerializationTests
{
    [Fact]
    public void LegacyGameBackups_SerializesWithCompatibleSnakeCaseNames()
    {
        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };
        var backups = new LegacyGameBackups(
            "中文游戏",
            [
                new LegacyBackupEntry(
                    "2026-04-17_22-07-50",
                    "Backup all",
                    ".\\save_data\\中文游戏\\2026-04-17_22-07-50.zip",
                    73177,
                    null,
                    "device-1")
            ],
            new Dictionary<string, string>
            {
                ["device-1"] = "2026-04-17_22-07-50"
            },
            0);

        var json = JsonSerializer.Serialize(backups, options);

        Assert.Contains("\"name\":", json);
        Assert.Contains("\"backups\":", json);
        Assert.Contains("\"device_heads\":", json);
        Assert.Contains("\"sync_version\":", json);
        Assert.Contains("\"device_id\":", json);
        Assert.Contains("中文游戏", json);
        Assert.DoesNotContain("\"Name\":", json);
        Assert.DoesNotContain("\"DeviceHeads\":", json);
        Assert.DoesNotContain("\"DeviceId\":", json);
        Assert.DoesNotContain("\\u4E2D", json, StringComparison.OrdinalIgnoreCase);
    }
}
