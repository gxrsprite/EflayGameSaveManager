using System.Text.Json;
using System.Text.Json.Serialization;

namespace EflayGameSaveManager.Core.Models;

public sealed class ManagerConfig
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("backup_path")]
    public string BackupPath { get; set; } = "./save_data";

    [JsonPropertyName("games")]
    public List<GameDefinition> Games { get; set; } = [];

    [JsonPropertyName("settings")]
    public AppSettings Settings { get; set; } = new();

    [JsonPropertyName("favorites")]
    public List<FavoriteNode> Favorites { get; set; } = [];

    [JsonPropertyName("quick_action")]
    public QuickActionSettings QuickAction { get; set; } = new();

    [JsonPropertyName("devices")]
    public Dictionary<string, DeviceDefinition> Devices { get; set; } = [];
}

public sealed class GameDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("save_paths")]
    public List<SaveUnitDefinition> SavePaths { get; set; } = [];

    [JsonPropertyName("game_paths")]
    public Dictionary<string, string> GamePaths { get; set; } = [];

    [JsonPropertyName("next_save_unit_id")]
    public int NextSaveUnitId { get; set; }

    [JsonPropertyName("cloud_sync_enabled")]
    public bool CloudSyncEnabled { get; set; }
}

public sealed class SaveUnitDefinition
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("unit_type")]
    public SaveUnitType UnitType { get; set; }

    [JsonPropertyName("paths")]
    public Dictionary<string, string> Paths { get; set; } = [];

    [JsonPropertyName("delete_before_apply")]
    public bool DeleteBeforeApply { get; set; }
}

public sealed class AppSettings
{
    [JsonPropertyName("locale")]
    public string Locale { get; set; } = string.Empty;

    [JsonPropertyName("log_to_file")]
    public bool LogToFile { get; set; }

    [JsonPropertyName("cloud_settings")]
    public CloudSettings CloudSettings { get; set; } = new();
}

public sealed class CloudSettings
{
    [JsonPropertyName("always_sync")]
    public bool AlwaysSync { get; set; }

    [JsonPropertyName("auto_sync_interval")]
    public int AutoSyncInterval { get; set; }

    [JsonPropertyName("root_path")]
    public string RootPath { get; set; } = string.Empty;

    [JsonPropertyName("backend")]
    public CloudBackendSettings Backend { get; set; } = new();

    [JsonPropertyName("max_concurrency")]
    public int MaxConcurrency { get; set; }
}

public sealed class CloudBackendSettings
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [JsonPropertyName("bucket")]
    public string Bucket { get; set; } = string.Empty;

    [JsonPropertyName("region")]
    public string Region { get; set; } = string.Empty;

    [JsonPropertyName("access_key_id")]
    public string AccessKeyId { get; set; } = string.Empty;

    [JsonPropertyName("secret_access_key")]
    public string SecretAccessKey { get; set; } = string.Empty;
}

public sealed class FavoriteNode
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("is_leaf")]
    public bool IsLeaf { get; set; }

    [JsonPropertyName("children")]
    public List<FavoriteNode>? Children { get; set; }
}

public sealed class QuickActionSettings
{
    [JsonPropertyName("quick_action_game")]
    public JsonElement QuickActionGame { get; set; }

    [JsonPropertyName("hotkeys")]
    public HotkeySettings Hotkeys { get; set; } = new();
}

public sealed class HotkeySettings
{
    [JsonPropertyName("apply")]
    public List<string> Apply { get; set; } = [];

    [JsonPropertyName("backup")]
    public List<string> Backup { get; set; } = [];
}

public sealed class DeviceDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
