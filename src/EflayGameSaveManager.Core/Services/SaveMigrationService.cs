using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Serialization;

namespace EflayGameSaveManager.Core.Services;

public sealed class SaveMigrationService
{
    private const string ManifestPath = "migration-manifest.json";
    private const string ConfigPath = "config/GameSaveManager.config.json";
    private readonly ArchiveTransferService _archiveTransferService;
    private readonly GameSaveManagerConfigurationService _configurationService;
    private readonly GameLibraryService _gameLibraryService;

    private static readonly ManagerJsonSerializerContext SerializerContext =
        new(new JsonSerializerOptions(ManagerJsonSerializerContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

    public SaveMigrationService(
        ArchiveTransferService archiveTransferService,
        GameSaveManagerConfigurationService configurationService,
        GameLibraryService gameLibraryService)
    {
        _archiveTransferService = archiveTransferService;
        _configurationService = configurationService;
        _gameLibraryService = gameLibraryService;
    }

    public async Task<SaveMigrationResult> CreatePackageAsync(
        ManagerConfig config,
        AppSnapshot snapshot,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var stagingRoot = Path.Combine(Path.GetTempPath(), "EflayGameSaveManager", "migration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);

        try
        {
            var savesRoot = Path.Combine(stagingRoot, "saves");
            Directory.CreateDirectory(savesRoot);
            var manifest = new SaveMigrationManifest
            {
                CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                SourceDeviceId = snapshot.CurrentDevice.DeviceId,
                SourceDeviceName = snapshot.CurrentDevice.DeviceName
            };
            var messages = new List<string>();

            for (var index = 0; index < snapshot.Games.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var game = snapshot.Games[index];
                var archivePath = Path.Combine(savesRoot, $"{index + 1:D4}-{CloudStoragePathHelper.SanitizeSegment(game.Name)}.zip");
                var gameWorkRoot = Path.Combine(stagingRoot, "work", index.ToString("D4"));
                Directory.CreateDirectory(gameWorkRoot);

                try
                {
                    var sourceArchive = _archiveTransferService.CreateCurrentDeviceArchive(game, snapshot.CurrentDevice, gameWorkRoot);
                    if (!File.Exists(sourceArchive))
                    {
                        messages.Add($"Skipped '{game.Name}': no save data was found.");
                        continue;
                    }

                    File.Copy(sourceArchive, archivePath, overwrite: true);
                    manifest.Games.Add(new SaveMigrationGameEntry
                    {
                        Name = game.Name,
                        Archive = $"saves/{Path.GetFileName(archivePath)}"
                    });
                }
                catch (Exception ex)
                {
                    messages.Add($"Skipped '{game.Name}': {ex.Message}");
                }
            }

            await _configurationService.SaveAsync(Path.Combine(stagingRoot, ConfigPath), config, cancellationToken);
            var manifestJson = JsonSerializer.Serialize(manifest, SerializerContext.SaveMigrationManifest);
            await File.WriteAllTextAsync(Path.Combine(stagingRoot, ManifestPath), manifestJson, cancellationToken);

            var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            ZipFile.CreateFromDirectory(stagingRoot, outputPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            return new SaveMigrationResult(manifest.Games.Count, snapshot.Games.Count - manifest.Games.Count, messages);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }
        }
    }

    public async Task<SaveMigrationResult> RestorePackageAsync(
        string packagePath,
        string configPath,
        string? machineName = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("Migration package was not found.", packagePath);
        }

        var extractRoot = Path.Combine(Path.GetTempPath(), "EflayGameSaveManager", "migration-restore", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractRoot);

        try
        {
            ZipFile.ExtractToDirectory(packagePath, extractRoot, overwriteFiles: true);
            var manifest = await ReadManifestAsync(Path.Combine(extractRoot, ManifestPath), cancellationToken);
            var packagedConfigPath = Path.Combine(extractRoot, ConfigPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(packagedConfigPath))
            {
                throw new InvalidDataException("Migration package does not contain a game configuration.");
            }

            BackupExistingConfig(configPath);
            var config = await _configurationService.LoadAsync(packagedConfigPath, cancellationToken);
            var snapshot = _gameLibraryService.CreateSnapshot(config, configPath, machineName);
            ApplyCurrentDeviceDefaults(config, snapshot);
            await _configurationService.SaveAsync(configPath, config, cancellationToken);

            var gamesByName = snapshot.Games.ToDictionary(game => game.Name, StringComparer.OrdinalIgnoreCase);
            var messages = new List<string>();
            var restored = 0;

            foreach (var entry in manifest.Games)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!gamesByName.TryGetValue(entry.Name, out var game))
                {
                    messages.Add($"Skipped '{entry.Name}': game is not present in the packaged configuration.");
                    continue;
                }

                var archive = Path.Combine(extractRoot, entry.Archive.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(archive))
                {
                    messages.Add($"Skipped '{entry.Name}': game archive is missing.");
                    continue;
                }

                try
                {
                    _archiveTransferService.RestoreCurrentDeviceArchive(archive, game, snapshot.CurrentDevice);
                    restored++;
                }
                catch (Exception ex)
                {
                    messages.Add($"Failed '{entry.Name}': {ex.Message}");
                }
            }

            return new SaveMigrationResult(restored, manifest.Games.Count - restored, messages);
        }
        finally
        {
            if (Directory.Exists(extractRoot))
            {
                Directory.Delete(extractRoot, recursive: true);
            }
        }
    }

    private static async Task<SaveMigrationManifest> ReadManifestAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(stream, SerializerContext.SaveMigrationManifest, cancellationToken)
               ?? throw new InvalidDataException("Migration manifest is invalid.");
    }

    private static void BackupExistingConfig(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return;
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupPath = $"{configPath}.before-migration-{timestamp}.bak";
        var suffix = 1;
        while (File.Exists(backupPath))
        {
            backupPath = $"{configPath}.before-migration-{timestamp}-{suffix++}.bak";
        }

        File.Copy(configPath, backupPath, overwrite: false);
    }

    private static void ApplyCurrentDeviceDefaults(ManagerConfig config, AppSnapshot snapshot)
    {
        foreach (var game in config.Games)
        {
            foreach (var saveUnit in game.SavePaths)
            {
                if (!saveUnit.Paths.ContainsKey(snapshot.CurrentDevice.DeviceId) && saveUnit.Paths.Values.FirstOrDefault() is { } path)
                {
                    saveUnit.Paths[snapshot.CurrentDevice.DeviceId] = path;
                }
            }

            if (!game.GamePaths.ContainsKey(snapshot.CurrentDevice.DeviceId) && game.GamePaths.Values.FirstOrDefault() is { } gamePath)
            {
                game.GamePaths[snapshot.CurrentDevice.DeviceId] = gamePath;
            }

            foreach (var launcher in game.ExternalLaunchers)
            {
                if (!launcher.Paths.ContainsKey(snapshot.CurrentDevice.DeviceId) && launcher.Paths.Values.FirstOrDefault() is { } launcherPath)
                {
                    launcher.Paths[snapshot.CurrentDevice.DeviceId] = launcherPath;
                }
            }
        }
    }
}
