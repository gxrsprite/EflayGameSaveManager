using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Serialization;

namespace EflayGameSaveManager.Core.Services;

public sealed class LocalSaveService
{
    private readonly ArchiveTransferService _archiveTransferService = new();
    private readonly WinRegistryTransferService _registryTransferService = new();

    private static readonly ManagerJsonSerializerContext ManifestSerializerContext =
        new(new JsonSerializerOptions(ManagerJsonSerializerContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

    public async Task<IReadOnlyList<LocalGameBackup>> ListBackupsAsync(
        string gameName,
        string backupRoot,
        CurrentDeviceContext currentDevice)
    {
        var manifest = await TryLoadManifestAsync(backupRoot, gameName);
        if (manifest is null)
        {
            return [];
        }

        var currentHead = ResolveCurrentHead(manifest, currentDevice.DeviceId);
        return manifest.Backups
            .OrderByDescending(item => item.Date, StringComparer.Ordinal)
            .Select(item => new LocalGameBackup(
                gameName,
                item.Date,
                item.Describe,
                item.Path,
                item.Size,
                item.Parent,
                item.DeviceId,
                string.Equals(item.Date, currentHead, StringComparison.Ordinal),
                manifest.DeviceHeads.TryGetValue(item.DeviceId, out var head) &&
                string.Equals(item.Date, head, StringComparison.Ordinal)))
            .ToArray();
    }

    public async Task CreateBackupAsync(
        GameSnapshot game,
        string backupRoot,
        CurrentDeviceContext currentDevice)
    {
        var safeGameName = CloudStoragePathHelper.SanitizeSegment(game.Name);
        var gameDirectory = Path.Combine(backupRoot, safeGameName);
        Directory.CreateDirectory(gameDirectory);

        var workRoot = Path.Combine(Path.GetTempPath(), "EflayGameSaveManager", "local-backup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);

        try
        {
            var archivePath = _archiveTransferService.CreateCurrentDeviceArchive(game, currentDevice, workRoot);
            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var zipDestination = Path.Combine(gameDirectory, $"{timestamp}.zip");

            File.Copy(archivePath, zipDestination, overwrite: true);
            var zipSize = new FileInfo(zipDestination).Length;

            var manifest = await TryLoadManifestAsync(backupRoot, game.Name)
                           ?? new LegacyGameBackups(game.Name, [], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), 0);

            var backupEntry = new LegacyBackupEntry(
                timestamp,
                "Backup all",
                zipDestination,
                zipSize,
                null,
                currentDevice.DeviceId);

            var updatedBackups = manifest with
            {
                Backups = [.. manifest.Backups.Where(item => !(item.Date == timestamp && string.Equals(item.DeviceId, currentDevice.DeviceId, StringComparison.OrdinalIgnoreCase))), backupEntry],
                DeviceHeads = new Dictionary<string, string>(manifest.DeviceHeads, StringComparer.OrdinalIgnoreCase)
                {
                    [currentDevice.DeviceId] = timestamp
                }
            };

            await SaveManifestAsync(backupRoot, safeGameName, updatedBackups);
        }
        finally
        {
            if (Directory.Exists(workRoot))
            {
                Directory.Delete(workRoot, recursive: true);
            }
        }
    }

    public async Task DeleteBackupAsync(
        string gameName,
        string backupRoot,
        string date,
        string deviceId,
        CurrentDeviceContext currentDevice)
    {
        var safeGameName = CloudStoragePathHelper.SanitizeSegment(gameName);
        var manifest = await TryLoadManifestAsync(backupRoot, gameName)
                       ?? throw new InvalidOperationException($"No local backups found for '{gameName}'.");

        var backup = FindBackup(manifest, date, deviceId)
                     ?? throw new InvalidOperationException($"Local backup not found: {date}");

        if (File.Exists(backup.Path))
        {
            File.Delete(backup.Path);
        }

        var remainingBackups = manifest.Backups
            .Where(item => !ReferenceEquals(item, backup))
            .ToArray();

        var deviceHeads = new Dictionary<string, string>(manifest.DeviceHeads, StringComparer.OrdinalIgnoreCase);
        foreach (var headDeviceId in deviceHeads.Keys.ToArray())
        {
            if (!string.Equals(deviceHeads[headDeviceId], date, StringComparison.Ordinal))
            {
                continue;
            }

            var replacementHead = remainingBackups
                .Where(item => string.Equals(item.DeviceId, headDeviceId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Date, StringComparer.Ordinal)
                .FirstOrDefault()?.Date;

            if (string.IsNullOrWhiteSpace(replacementHead))
            {
                deviceHeads.Remove(headDeviceId);
            }
            else
            {
                deviceHeads[headDeviceId] = replacementHead;
            }
        }

        var updatedManifest = manifest with
        {
            Backups = remainingBackups,
            DeviceHeads = deviceHeads
        };

        await SaveManifestAsync(backupRoot, safeGameName, updatedManifest);

        if (remainingBackups.Length == 0)
        {
            var manifestPath = GetManifestPath(backupRoot, safeGameName);
            if (File.Exists(manifestPath))
            {
                File.Delete(manifestPath);
            }
        }
    }

    public Task RestoreBackupAsync(
        GameSnapshot game,
        string backupRoot,
        CurrentDeviceContext currentDevice,
        string date,
        string? deviceId = null)
    {
        var manifest = TryLoadManifestSync(backupRoot, game.Name)
                       ?? throw new InvalidOperationException($"No local backups found for '{game.Name}'.");

        var backup = FindBackup(manifest, date, deviceId)
                     ?? throw new InvalidOperationException($"Local backup not found: {date}");

        if (!File.Exists(backup.Path))
        {
            throw new FileNotFoundException($"Local backup zip not found: {backup.Path}");
        }

        _archiveTransferService.RestoreCurrentDeviceArchive(backup.Path, game, currentDevice);
        return Task.CompletedTask;
    }

    private static string GetGameDirectory(string backupRoot, string safeGameName)
    {
        return Path.Combine(backupRoot, safeGameName);
    }

    private static string GetManifestPath(string backupRoot, string safeGameName)
    {
        return Path.Combine(backupRoot, safeGameName, "Backups.json");
    }

    private async Task<LegacyGameBackups?> TryLoadManifestAsync(string backupRoot, string gameName)
    {
        var safeGameName = CloudStoragePathHelper.SanitizeSegment(gameName);
        var manifestPath = GetManifestPath(backupRoot, safeGameName);

        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(manifestPath);
        var manifest = JsonSerializer.Deserialize(json, ManagerJsonSerializerContext.Default.LegacyGameBackups);
        return manifest is null
            ? null
            : manifest with
            {
                Name = string.IsNullOrWhiteSpace(manifest.Name) ? gameName : manifest.Name,
                Backups = manifest.Backups ?? [],
                DeviceHeads = manifest.DeviceHeads ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
    }

    private LegacyGameBackups? TryLoadManifestSync(string backupRoot, string gameName)
    {
        var safeGameName = CloudStoragePathHelper.SanitizeSegment(gameName);
        var manifestPath = GetManifestPath(backupRoot, safeGameName);

        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize(json, ManagerJsonSerializerContext.Default.LegacyGameBackups);
        return manifest is null
            ? null
            : manifest with
            {
                Name = string.IsNullOrWhiteSpace(manifest.Name) ? gameName : manifest.Name,
                Backups = manifest.Backups ?? [],
                DeviceHeads = manifest.DeviceHeads ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
    }

    private async Task SaveManifestAsync(string backupRoot, string safeGameName, LegacyGameBackups manifest)
    {
        var gameDirectory = GetGameDirectory(backupRoot, safeGameName);
        Directory.CreateDirectory(gameDirectory);

        var manifestPath = GetManifestPath(backupRoot, safeGameName);
        var json = JsonSerializer.Serialize(manifest, ManifestSerializerContext.LegacyGameBackups);
        await File.WriteAllTextAsync(manifestPath, json);
    }

    private static string? ResolveCurrentHead(LegacyGameBackups backups, string deviceId)
    {
        if (backups.DeviceHeads.TryGetValue(deviceId, out var deviceHead) && !string.IsNullOrWhiteSpace(deviceHead))
        {
            return deviceHead;
        }

        return backups.Backups
            .Where(item => string.Equals(item.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Date, StringComparer.Ordinal)
            .FirstOrDefault()?.Date;
    }

    private static LegacyBackupEntry? FindBackup(LegacyGameBackups backups, string date, string? deviceId)
    {
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            var deviceMatch = backups.Backups.FirstOrDefault(item =>
                string.Equals(item.Date, date, StringComparison.Ordinal) &&
                string.Equals(item.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
            if (deviceMatch is not null)
            {
                return deviceMatch;
            }
        }

        return backups.Backups.FirstOrDefault(item => string.Equals(item.Date, date, StringComparison.Ordinal));
    }
}