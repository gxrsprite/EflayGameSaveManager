using System.Text.Encodings.Web;
using System.Text.Json;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Serialization;

namespace EflayGameSaveManager.Core.Services;

public sealed class CloudSyncService
{
    private readonly S3CompatibleCloudStorageClient _storageClient;
    private readonly SaveBackupService _backupService;
    private readonly ArchiveTransferService _archiveTransferService;
    private static readonly ManagerJsonSerializerContext CloudJsonSerializerContext =
        new(new JsonSerializerOptions(ManagerJsonSerializerContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

    public CloudSyncService(
        S3CompatibleCloudStorageClient storageClient,
        SaveBackupService backupService,
        ArchiveTransferService archiveTransferService)
    {
        _storageClient = storageClient;
        _backupService = backupService;
        _archiveTransferService = archiveTransferService;
    }

    public async Task<CloudUploadResult> UploadGameCurrentSaveAsync(
        GameSnapshot game,
        CurrentDeviceContext currentDevice,
        CloudSettings cloudSettings,
        CancellationToken cancellationToken = default)
    {
        AppLogger.Info(
            $"Cloud upload-current start: game={game.Name}, device={currentDevice.DeviceName}[{currentDevice.DeviceId}], endpoint={cloudSettings.Backend.Endpoint}, bucket={cloudSettings.Backend.Bucket}, root={cloudSettings.RootPath}");
        var rootKey = GetGameRootKey(cloudSettings, game.Name);
        var workRoot = Path.Combine(Path.GetTempPath(), "EflayGameSaveManager", "upload", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);

        try
        {
            var archivePath = _archiveTransferService.CreateCurrentDeviceArchive(game, currentDevice, workRoot);
            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var archiveKey = CloudStoragePathHelper.CombineKey(rootKey, $"{timestamp}.zip");
            var saveDataRootKey = GetSaveDataRootKey(cloudSettings);
            await _storageClient.UploadFileAsync(cloudSettings.Backend, archiveKey, archivePath, cancellationToken);

            var backups = await LoadGameBackupsAsync(game.Name, cloudSettings, cancellationToken);
            var backupEntry = new LegacyBackupEntry(
                timestamp,
                string.Empty,
                BuildBackupRelativePath(saveDataRootKey, game.Name, timestamp),
                new FileInfo(archivePath).Length,
                null,
                currentDevice.DeviceId);

            var updatedBackups = backups with
            {
                Backups = [.. backups.Backups.Where(item => !(item.Date == timestamp && item.DeviceId == currentDevice.DeviceId)), backupEntry],
                DeviceHeads = new Dictionary<string, string>(backups.DeviceHeads, StringComparer.OrdinalIgnoreCase)
                {
                    [currentDevice.DeviceId] = timestamp
                }
            };

            var manifestJson = JsonSerializer.Serialize(updatedBackups, CloudJsonSerializerContext.LegacyGameBackups);
            await _storageClient.UploadUtf8JsonAsync(
                cloudSettings.Backend,
                CloudStoragePathHelper.CombineKey(rootKey, "Backups.json"),
                manifestJson,
                cancellationToken);

            await UpdateSyncStateAsync(cloudSettings, currentDevice, game.Name, timestamp, cancellationToken);
            AppLogger.Info(
                $"Cloud upload-current completed: game={game.Name}, device={currentDevice.DeviceId}, archiveKey={archiveKey}, size={new FileInfo(archivePath).Length}, rootKey={rootKey}");

            return new CloudUploadResult(
                rootKey,
                2,
                new FileInfo(archivePath).Length + manifestJson.Length,
                DateTimeOffset.UtcNow);
        }
        finally
        {
            if (Directory.Exists(workRoot))
            {
                Directory.Delete(workRoot, recursive: true);
            }
        }
    }

    public async Task<GameCloudStatus> GetGameCurrentStatusAsync(
        GameSnapshot game,
        CurrentDeviceContext currentDevice,
        CloudSettings cloudSettings,
        CancellationToken cancellationToken = default)
    {
        var backups = await TryLoadGameBackupsAsync(game.Name, cloudSettings, cancellationToken);
        var currentHead = backups is null ? null : ResolveCurrentHead(backups, currentDevice.DeviceId);

        return new GameCloudStatus(
            game.Name,
            !string.IsNullOrWhiteSpace(currentHead),
            GetGameRootKey(cloudSettings, game.Name),
            ParseBackupTimestamp(currentHead),
            backups?.Backups.Count ?? 0,
            currentHead);
    }

    public async Task<IReadOnlyList<CloudGameBackup>> ListGameBackupsAsync(
        GameSnapshot game,
        CurrentDeviceContext currentDevice,
        CloudSettings cloudSettings,
        CancellationToken cancellationToken = default)
    {
        var backups = await TryLoadGameBackupsAsync(game.Name, cloudSettings, cancellationToken);
        if (backups is null)
        {
            return [];
        }

        var currentHead = ResolveCurrentHead(backups, currentDevice.DeviceId);
        return backups.Backups
            .OrderByDescending(item => item.Date, StringComparer.Ordinal)
            .Select(item => new CloudGameBackup(
                game.Name,
                item.Date,
                item.Describe,
                item.Path,
                item.Size,
                item.Parent,
                item.DeviceId,
                string.Equals(item.Date, currentHead, StringComparison.Ordinal)))
            .ToArray();
    }

    public async Task<CloudDownloadResult> RestoreGameCurrentSaveAsync(
        GameSnapshot game,
        CurrentDeviceContext currentDevice,
        CloudSettings cloudSettings,
        CancellationToken cancellationToken = default)
    {
        AppLogger.Info(
            $"Cloud restore-current start: game={game.Name}, device={currentDevice.DeviceName}[{currentDevice.DeviceId}], endpoint={cloudSettings.Backend.Endpoint}, bucket={cloudSettings.Backend.Bucket}, root={cloudSettings.RootPath}");
        var backups = await TryLoadGameBackupsAsync(game.Name, cloudSettings, cancellationToken)
                      ?? throw new InvalidOperationException($"No cloud backups found for '{game.Name}'.");
        var currentBackup = ResolveCurrentBackup(backups, currentDevice.DeviceId)
                            ?? throw new InvalidOperationException($"No cloud backup head found for '{game.Name}' on device '{currentDevice.DeviceName}'.");
        AppLogger.Info(
            $"Cloud restore-current resolved head: game={game.Name}, device={currentDevice.DeviceId}, backupDate={currentBackup.Date}, backupPath={currentBackup.Path}");

        return await RestoreGameBackupAsync(game, currentDevice, cloudSettings, currentBackup, cancellationToken);
    }

    public async Task<CloudDownloadResult> RestoreGameBackupAsync(
        GameSnapshot game,
        CurrentDeviceContext currentDevice,
        CloudSettings cloudSettings,
        string backupDate,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupDate))
        {
            throw new InvalidOperationException("No cloud backup is selected.");
        }

        var backups = await TryLoadGameBackupsAsync(game.Name, cloudSettings, cancellationToken)
                      ?? throw new InvalidOperationException($"No cloud backups found for '{game.Name}'.");
        var backup = backups.Backups.FirstOrDefault(item => string.Equals(item.Date, backupDate, StringComparison.Ordinal))
                     ?? throw new InvalidOperationException($"Cloud backup not found: {backupDate}");
        AppLogger.Info(
            $"Cloud restore-backup resolved entry: game={game.Name}, backupDate={backupDate}, device={backup.DeviceId}, backupPath={backup.Path}, size={backup.Size}");

        return await RestoreGameBackupAsync(game, currentDevice, cloudSettings, backup, cancellationToken);
    }

    public async Task<CloudDownloadResult> DownloadGameBackupArchiveAsync(
        GameSnapshot game,
        CloudSettings cloudSettings,
        string backupDate,
        string destinationPath,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupDate))
        {
            throw new InvalidOperationException("No cloud backup is selected.");
        }

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new InvalidOperationException("Download destination is empty.");
        }

        if (File.Exists(destinationPath) && !overwrite)
        {
            throw new IOException($"File already exists: {destinationPath}");
        }

        var backups = await TryLoadGameBackupsAsync(game.Name, cloudSettings, cancellationToken)
                      ?? throw new InvalidOperationException($"No cloud backups found for '{game.Name}'.");
        var backup = backups.Backups.FirstOrDefault(item => string.Equals(item.Date, backupDate, StringComparison.Ordinal))
                     ?? throw new InvalidOperationException($"Cloud backup not found: {backupDate}");
        var archiveKey = ResolveArchiveKey(backup, cloudSettings, game.Name);

        await _storageClient.DownloadFileAsync(cloudSettings.Backend, archiveKey, destinationPath, cancellationToken);

        return new CloudDownloadResult(
            archiveKey,
            1,
            new FileInfo(destinationPath).Length,
            DateTimeOffset.UtcNow);
    }

    public async Task DeleteGameBackupAsync(
        GameSnapshot game,
        CurrentDeviceContext currentDevice,
        CloudSettings cloudSettings,
        string backupDate,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupDate))
        {
            throw new InvalidOperationException("No cloud backup is selected.");
        }

        var backups = await TryLoadGameBackupsAsync(game.Name, cloudSettings, cancellationToken)
                      ?? throw new InvalidOperationException($"No cloud backups found for '{game.Name}'.");
        var backup = backups.Backups.FirstOrDefault(item => string.Equals(item.Date, backupDate, StringComparison.Ordinal))
                     ?? throw new InvalidOperationException($"Cloud backup not found: {backupDate}");
        var archiveKey = ResolveArchiveKey(backup, cloudSettings, game.Name);

        await _storageClient.DeleteObjectAsync(cloudSettings.Backend, archiveKey, cancellationToken);

        var remainingBackups = backups.Backups
            .Where(item => !string.Equals(item.Date, backupDate, StringComparison.Ordinal))
            .ToArray();
        var deviceHeads = new Dictionary<string, string>(backups.DeviceHeads, StringComparer.OrdinalIgnoreCase);

        foreach (var deviceId in deviceHeads.Keys.ToArray())
        {
            if (!string.Equals(deviceHeads[deviceId], backupDate, StringComparison.Ordinal))
            {
                continue;
            }

            var replacementHead = remainingBackups
                .Where(item => string.Equals(item.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Date, StringComparer.Ordinal)
                .FirstOrDefault()?.Date;

            if (string.IsNullOrWhiteSpace(replacementHead))
            {
                deviceHeads.Remove(deviceId);
            }
            else
            {
                deviceHeads[deviceId] = replacementHead;
            }
        }

        var updatedBackups = backups with
        {
            Backups = remainingBackups,
            DeviceHeads = deviceHeads
        };

        var rootKey = GetGameRootKey(cloudSettings, game.Name);
        var manifestJson = JsonSerializer.Serialize(updatedBackups, CloudJsonSerializerContext.LegacyGameBackups);
        await _storageClient.UploadUtf8JsonAsync(
            cloudSettings.Backend,
            CloudStoragePathHelper.CombineKey(rootKey, "Backups.json"),
            manifestJson,
            cancellationToken);
    }

    private async Task<CloudDownloadResult> RestoreGameBackupAsync(
        GameSnapshot game,
        CurrentDeviceContext currentDevice,
        CloudSettings cloudSettings,
        LegacyBackupEntry backup,
        CancellationToken cancellationToken)
    {
        var archiveKey = ResolveArchiveKey(backup, cloudSettings, game.Name);
        AppLogger.Info(
            $"Cloud restore-backup start: game={game.Name}, device={currentDevice.DeviceName}[{currentDevice.DeviceId}], backupDate={backup.Date}, backupPath={backup.Path}, archiveKey={archiveKey}");

        var workRoot = Path.Combine(Path.GetTempPath(), "EflayGameSaveManager", "download", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);
        var archivePath = Path.Combine(workRoot, "cloud-save.zip");

        try
        {
            await _storageClient.DownloadFileAsync(cloudSettings.Backend, archiveKey, archivePath, cancellationToken);
            _archiveTransferService.RestoreCurrentDeviceArchive(archivePath, game, currentDevice);
            await UpdateSyncStateAsync(cloudSettings, currentDevice, game.Name, backup.Date, cancellationToken);
            AppLogger.Info(
                $"Cloud restore-backup completed: game={game.Name}, backupDate={backup.Date}, archiveKey={archiveKey}, downloadedBytes={new FileInfo(archivePath).Length}");

            return new CloudDownloadResult(
                archiveKey,
                1,
                new FileInfo(archivePath).Length,
                DateTimeOffset.UtcNow);
        }
        finally
        {
            if (Directory.Exists(workRoot))
            {
                Directory.Delete(workRoot, recursive: true);
            }
        }
    }

    public async Task<CloudUploadResult> UploadAllBackupsAsync(
        AppSnapshot snapshot,
        CloudSettings cloudSettings,
        CancellationToken cancellationToken = default)
    {
        var objectCount = 0;
        long byteCount = 0;

        foreach (var game in snapshot.Games)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!game.CloudSyncEnabled)
            {
                continue;
            }

            var uploadStats = await UploadGameCurrentSaveAsync(game, snapshot.CurrentDevice, cloudSettings, cancellationToken);
            objectCount += uploadStats.UploadedObjectCount;
            byteCount += uploadStats.UploadedByteCount;
        }

        return new CloudUploadResult(
            GetSaveDataRootKey(cloudSettings),
            objectCount,
            byteCount,
            DateTimeOffset.UtcNow);
    }

    private async Task<LegacyGameBackups?> TryLoadGameBackupsAsync(
        string gameName,
        CloudSettings cloudSettings,
        CancellationToken cancellationToken)
    {
        var rootKey = GetGameRootKey(cloudSettings, gameName);
        var json = await _storageClient.TryDownloadUtf8StringAsync(
            cloudSettings.Backend,
            CloudStoragePathHelper.CombineKey(rootKey, "Backups.json"),
            cancellationToken);

        return json is null
            ? null
            : NormalizeGameBackups(
                JsonSerializer.Deserialize(json, ManagerJsonSerializerContext.Default.LegacyGameBackups),
                gameName);
    }

    private async Task<LegacyGameBackups> LoadGameBackupsAsync(
        string gameName,
        CloudSettings cloudSettings,
        CancellationToken cancellationToken)
    {
        var existing = await TryLoadGameBackupsAsync(gameName, cloudSettings, cancellationToken);
        return existing ?? new LegacyGameBackups(
            gameName,
            [],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            0);
    }

    private async Task UpdateSyncStateAsync(
        CloudSettings cloudSettings,
        CurrentDeviceContext currentDevice,
        string gameName,
        string head,
        CancellationToken cancellationToken)
    {
        var syncState = await LoadSyncStateAsync(cloudSettings, currentDevice, cancellationToken);
        var syncedAt = DateTimeOffset.UtcNow;

        var updatedGames = new Dictionary<string, LegacySyncStateItem>(syncState.Games, StringComparer.Ordinal)
        {
            [gameName] = new LegacySyncStateItem(head, head, "success", syncedAt, "none")
        };

        var updatedState = syncState with
        {
            CurrentDeviceId = currentDevice.DeviceId,
            ConfigState = new LegacySyncStateItem(null, null, "success", syncedAt, "none"),
            Games = updatedGames
        };

        var json = JsonSerializer.Serialize(updatedState, CloudJsonSerializerContext.LegacySyncState);
        await _storageClient.UploadUtf8JsonAsync(
            cloudSettings.Backend,
            CloudStoragePathHelper.CombineKey(
                GetSaveDataRootKey(cloudSettings),
                "sync_state.json"),
            json,
            cancellationToken);
    }

    private async Task<LegacySyncState> LoadSyncStateAsync(
        CloudSettings cloudSettings,
        CurrentDeviceContext currentDevice,
        CancellationToken cancellationToken)
    {
        var key = CloudStoragePathHelper.CombineKey(
            GetSaveDataRootKey(cloudSettings),
            "sync_state.json");
        var json = await _storageClient.TryDownloadUtf8StringAsync(cloudSettings.Backend, key, cancellationToken);
        if (json is not null)
        {
            var existing = NormalizeSyncState(
                JsonSerializer.Deserialize(json, ManagerJsonSerializerContext.Default.LegacySyncState),
                currentDevice,
                cloudSettings);
            if (existing is not null)
            {
                return existing;
            }
        }

        return new LegacySyncState(
            1,
            BuildBackendFingerprint(cloudSettings),
            currentDevice.DeviceId,
            new LegacySyncStateItem(null, null, "none", null, "none"),
            new Dictionary<string, LegacySyncStateItem>(StringComparer.Ordinal));
    }

    private static string? ResolveCurrentHead(LegacyGameBackups backups, string deviceId)
    {
        return ResolveCurrentBackup(backups, deviceId)?.Date;
    }

    private static LegacyBackupEntry? ResolveCurrentBackup(LegacyGameBackups backups, string deviceId)
    {
        if (backups.DeviceHeads.TryGetValue(deviceId, out var deviceHead) && !string.IsNullOrWhiteSpace(deviceHead))
        {
            return backups.Backups
                .Where(item => string.Equals(item.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => string.Equals(item.Date, deviceHead, StringComparison.Ordinal))
                .ThenByDescending(item => item.Date, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        var deviceBackup = backups.Backups
            .Where(item => string.Equals(item.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Date, StringComparer.Ordinal)
            .FirstOrDefault();

        if (deviceBackup is not null)
        {
            return deviceBackup;
        }

        return backups.Backups
            .OrderByDescending(item => item.Date, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static LegacyGameBackups? NormalizeGameBackups(LegacyGameBackups? backups, string gameName)
    {
        if (backups is null)
        {
            return null;
        }

        return backups with
        {
            Name = string.IsNullOrWhiteSpace(backups.Name) ? gameName : backups.Name,
            Backups = backups.Backups ?? [],
            DeviceHeads = backups.DeviceHeads ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private static LegacySyncState? NormalizeSyncState(
        LegacySyncState? state,
        CurrentDeviceContext currentDevice,
        CloudSettings cloudSettings)
    {
        if (state is null)
        {
            return null;
        }

        return state with
        {
            BackendFingerprint = string.IsNullOrWhiteSpace(state.BackendFingerprint)
                ? BuildBackendFingerprint(cloudSettings)
                : state.BackendFingerprint,
            CurrentDeviceId = string.IsNullOrWhiteSpace(state.CurrentDeviceId)
                ? currentDevice.DeviceId
                : state.CurrentDeviceId,
            ConfigState = state.ConfigState ?? new LegacySyncStateItem(null, null, "none", null, "none"),
            Games = state.Games ?? new Dictionary<string, LegacySyncStateItem>(StringComparer.Ordinal)
        };
    }

    private static DateTimeOffset? ParseBackupTimestamp(string? value)
    {
        return DateTimeOffset.TryParseExact(
            value,
            "yyyy-MM-dd_HH-mm-ss",
            null,
            System.Globalization.DateTimeStyles.AssumeLocal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string BuildBackendFingerprint(CloudSettings cloudSettings)
    {
        var backend = cloudSettings.Backend;
        var backendJson =
            $"{{\"type\":\"{backend.Type}\",\"endpoint\":\"{backend.Endpoint}\",\"bucket\":\"{backend.Bucket}\",\"region\":\"{backend.Region}\",\"access_key_id\":\"{backend.AccessKeyId}\",\"secret_access_key\":\"{backend.SecretAccessKey}\"}}";
        return $"/{CloudStoragePathHelper.NormalizeRootPath(cloudSettings.RootPath)}|{backendJson}";
    }

    private static string GetSaveDataRootKey(CloudSettings cloudSettings)
    {
        return CloudStoragePathHelper.CombineKey(cloudSettings.RootPath, "save_data");
    }

    private static string GetGameRootKey(CloudSettings cloudSettings, string gameName)
    {
        return CloudStoragePathHelper.CombineKey(
            GetSaveDataRootKey(cloudSettings),
            gameName);
    }

    private static string BuildBackupRelativePath(string saveDataRootKey, string gameName, string timestamp)
    {
        _ = saveDataRootKey;
        return $".\\save_data\\{gameName}\\{timestamp}.zip";
    }

    private static string ResolveArchiveKey(
        LegacyBackupEntry backupEntry,
        CloudSettings cloudSettings,
        string gameName)
    {
        if (!string.IsNullOrWhiteSpace(backupEntry.Path))
        {
            var relativePath = NormalizeLegacyArchivePath(backupEntry.Path, cloudSettings);

            if (relativePath.StartsWith("./", StringComparison.Ordinal))
            {
                relativePath = relativePath[2..];
            }
            else if (relativePath.StartsWith(".\\", StringComparison.Ordinal))
            {
                relativePath = relativePath[2..];
            }

            const string legacyPrefix = "save_data\\";
            if (relativePath.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var configuredSaveDataRoot = GetSaveDataRootKey(cloudSettings).Replace('/', '\\');
                relativePath = configuredSaveDataRoot + "\\" + relativePath[legacyPrefix.Length..];
            }

            var parts = relativePath
                .Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 0)
            {
                return string.Join("/", parts);
            }
        }

        var gameRootKey = GetGameRootKey(cloudSettings, gameName);
        return CloudStoragePathHelper.CombineKey(gameRootKey, $"{backupEntry.Date}.zip");
    }

    private static string NormalizeLegacyArchivePath(string originalPath, CloudSettings cloudSettings)
    {
        var configuredSaveDataRoot = GetSaveDataRootKey(cloudSettings).Replace('/', '\\');
        var normalized = originalPath
            .Replace('/', '\\')
            .Trim();

        if (normalized.StartsWith(".\\", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }
        else if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        // Legacy records may store an absolute local path such as:
        // E:\Program Files\RGSM\.\save_data\WRC4\yyyy-MM-dd_HH-mm-ss.zip
        // Convert it back to cloud-root-relative form.
        const string saveDataMarker = "\\save_data\\";
        var markerIndex = normalized.IndexOf(saveDataMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            normalized = "save_data\\" + normalized[(markerIndex + saveDataMarker.Length)..];
        }

        var parts = normalized
            .Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.Equals(part, ".", StringComparison.Ordinal))
            .ToArray();
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        normalized = string.Join("\\", parts);

        if (normalized.StartsWith("save_data\\", StringComparison.OrdinalIgnoreCase))
        {
            normalized = configuredSaveDataRoot + "\\" + normalized["save_data\\".Length..];
        }

        return normalized;
    }
}
