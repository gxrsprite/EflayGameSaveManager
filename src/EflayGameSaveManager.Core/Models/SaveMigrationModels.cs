using System.Text.Json.Serialization;

namespace EflayGameSaveManager.Core.Models;

public sealed class SaveMigrationManifest
{
    [JsonPropertyName("format_version")]
    public int FormatVersion { get; set; } = 1;

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("source_device_id")]
    public string SourceDeviceId { get; set; } = string.Empty;

    [JsonPropertyName("source_device_name")]
    public string SourceDeviceName { get; set; } = string.Empty;

    [JsonPropertyName("games")]
    public List<SaveMigrationGameEntry> Games { get; set; } = [];
}

public sealed class SaveMigrationGameEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("archive")]
    public string Archive { get; set; } = string.Empty;
}

public sealed record SaveMigrationResult(
    int ProcessedGames,
    int SkippedGames,
    IReadOnlyList<string> Messages);
