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
    bool IsCurrentDeviceHead);

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
    string Name,
    IReadOnlyList<LegacyBackupEntry> Backups,
    IReadOnlyDictionary<string, string> DeviceHeads,
    int SyncVersion);

public sealed record LegacyBackupEntry(
    string Date,
    string Describe,
    string Path,
    long Size,
    string? Parent,
    string DeviceId);

public sealed record LegacySyncState(
    int SchemaVersion,
    string BackendFingerprint,
    string CurrentDeviceId,
    LegacySyncStateItem ConfigState,
    IReadOnlyDictionary<string, LegacySyncStateItem> Games);

public sealed record LegacySyncStateItem(
    string? LastKnownLocalHead,
    string? LastKnownRemoteHead,
    string LastSyncResult,
    DateTimeOffset? LastSyncAt,
    string PendingAction);
