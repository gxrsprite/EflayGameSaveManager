using System.Text.Json.Serialization;

namespace EflayGameSaveManager.Core.Models;

public sealed record CloudUploadResult(
    string RootKey,
    int UploadedObjectCount,
    long UploadedByteCount,
    DateTimeOffset CompletedAtUtc);

public sealed record CloudDownloadResult(
    string RootKey,
    int DownloadedObjectCount,
    long DownloadedByteCount,
    DateTimeOffset CompletedAtUtc);

public sealed record CloudManifestRebuildResult(
    string RootKey,
    int BackupCount,
    int PreservedDeviceCount,
    DateTimeOffset CompletedAtUtc);

public sealed record CloudObjectInfo(
    string Key,
    long Size,
    DateTimeOffset? LastModifiedAtUtc);

public sealed record GameCloudStatus(
    string GameName,
    bool IsAvailable,
    string RootKey,
    DateTimeOffset? SyncedAtUtc,
    int BackupCount,
    string? CurrentHead);

public sealed record CloudGameBackup(
    string GameName,
    string Date,
    string Describe,
    string Path,
    long Size,
    string? Parent,
    string DeviceId,
    bool IsCurrentDeviceHead,
    bool IsDeviceHead);

public sealed record GameCloudManifest(
    string GameName,
    string RootKey,
    DateTimeOffset SyncedAtUtc,
    IReadOnlyList<ManifestSaveUnit> SaveUnits);

public sealed record ManifestSaveUnit(
    int Id,
    SaveUnitType UnitType,
    IReadOnlyList<ManifestDeviceEntry> Paths);

public sealed record ManifestDeviceEntry(
    string DeviceId,
    string DeviceName,
    string SourcePath,
    string CloudKeyPrefix);

public sealed record BackupBatchManifest(
    string BatchId,
    string RootKey,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<BackupBatchGameEntry> Games);

public sealed record BackupBatchGameEntry(
    string Name,
    string BackupDirectory,
    string CloudKeyPrefix);

public sealed record LegacyGameBackups(
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("backups")]
    IReadOnlyList<LegacyBackupEntry> Backups,
    [property: JsonPropertyName("device_heads")]
    IReadOnlyDictionary<string, string> DeviceHeads,
    [property: JsonPropertyName("sync_version")]
    int SyncVersion);

public sealed record LegacyBackupEntry(
    [property: JsonPropertyName("date")]
    string Date,
    [property: JsonPropertyName("describe")]
    string Describe,
    [property: JsonPropertyName("path")]
    string Path,
    [property: JsonPropertyName("size")]
    long Size,
    [property: JsonPropertyName("parent")]
    string? Parent,
    [property: JsonPropertyName("device_id")]
    string DeviceId);

public sealed record LegacySyncState(
    [property: JsonPropertyName("schema_version")]
    int SchemaVersion,
    [property: JsonPropertyName("backend_fingerprint")]
    string BackendFingerprint,
    [property: JsonPropertyName("current_device_id")]
    string CurrentDeviceId,
    [property: JsonPropertyName("config_state")]
    LegacySyncStateItem ConfigState,
    [property: JsonPropertyName("games")]
    IReadOnlyDictionary<string, LegacySyncStateItem> Games);

public sealed record LegacySyncStateItem(
    [property: JsonPropertyName("last_known_local_head")]
    string? LastKnownLocalHead,
    [property: JsonPropertyName("last_known_remote_head")]
    string? LastKnownRemoteHead,
    [property: JsonPropertyName("last_sync_result")]
    string LastSyncResult,
    [property: JsonPropertyName("last_sync_at")]
    DateTimeOffset? LastSyncAt,
    [property: JsonPropertyName("pending_action")]
    string PendingAction);
