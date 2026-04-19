using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services;
using System.Text.Encodings.Web;
using System.Text.Json;

var exitCode = await CloudTool.RunAsync(args);
return exitCode;

internal static class CloudTool
{
    private static readonly JsonSerializerOptions BackupsJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = ParseArgs(args);
            if (!options.TryGetValue("action", out var action) || string.IsNullOrWhiteSpace(action))
            {
                PrintUsage();
                return 2;
            }

            var configPath = options.TryGetValue("config", out var explicitConfigPath) && !string.IsNullOrWhiteSpace(explicitConfigPath)
                ? Path.GetFullPath(explicitConfigPath)
                : new GameSaveManagerConfigurationService().FindConfigPath();
            var gameName = options.GetValueOrDefault("game") ?? string.Empty;

            var configurationService = new GameSaveManagerConfigurationService();
            var config = await configurationService.LoadAsync(configPath);
            var configDirectory = Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory;
            var currentDeviceService = new CurrentDeviceService();
            var gameLibraryService = new GameLibraryService(
                new EnvironmentTokenResolver(),
                currentDeviceService,
                new AppRuntimeSettingsService());

            if (string.Equals(action, "init-backup-dirs", StringComparison.OrdinalIgnoreCase))
            {
                var backupRoot = gameLibraryService.ResolveBackupRoot(configDirectory, config.BackupPath);
                var result = InitializeBackupFolders(config.Games, backupRoot);
                Console.WriteLine($"Backup root: {backupRoot}");
                Console.WriteLine($"Game folders created: {result.CreatedFolders}");
                Console.WriteLine($"Game folders skipped: {result.SkippedFolders}");
                Console.WriteLine($"Backups.json created: {result.CreatedBackupsJson}");
                Console.WriteLine($"Backups.json skipped: {result.SkippedBackupsJson}");
                return 0;
            }

            var snapshot = gameLibraryService.CreateSnapshot(config, configPath);
            var cloudSettings = config.Settings.CloudSettings;
            EnsureCloudConfigured(cloudSettings);

            var cloudSyncService = new CloudSyncService(
                new S3CompatibleCloudStorageClient(),
                new SaveBackupService(),
                new ArchiveTransferService());

            if (string.Equals(action, "status", StringComparison.OrdinalIgnoreCase))
            {
                var game = ResolveGame(snapshot, gameName);
                var status = await cloudSyncService.GetGameCurrentStatusAsync(game, snapshot.CurrentDevice, cloudSettings);
                var backups = await cloudSyncService.ListGameBackupsAsync(game, snapshot.CurrentDevice, cloudSettings);
                Console.WriteLine($"Game: {status.GameName}");
                Console.WriteLine($"Cloud root: {status.RootKey}");
                Console.WriteLine($"Current device: {snapshot.CurrentDevice.DeviceName} [{snapshot.CurrentDevice.DeviceId}]");
                Console.WriteLine($"Current head: {status.CurrentHead ?? "-"}");
                Console.WriteLine($"Backup count: {status.BackupCount}");
                foreach (var backup in backups.Take(20))
                {
                    Console.WriteLine($"{backup.Date} | {FormatSize(backup.Size)} | {backup.DeviceId} | {(backup.IsCurrentDeviceHead ? "current" : "-")}");
                }

                return 0;
            }

            if (string.Equals(action, "upload-current", StringComparison.OrdinalIgnoreCase))
            {
                var game = ResolveGame(snapshot, gameName);
                var result = await cloudSyncService.UploadGameCurrentSaveAsync(game, snapshot.CurrentDevice, cloudSettings);
                Console.WriteLine($"Uploaded current save for {game.Name}");
                Console.WriteLine($"Cloud root: {result.RootKey}");
                Console.WriteLine($"Objects: {result.UploadedObjectCount}");
                Console.WriteLine($"Bytes: {result.UploadedByteCount}");
                return 0;
            }

            if (string.Equals(action, "restore-current", StringComparison.OrdinalIgnoreCase))
            {
                var game = ResolveGame(snapshot, gameName);
                var result = await cloudSyncService.RestoreGameCurrentSaveAsync(game, snapshot.CurrentDevice, cloudSettings);
                Console.WriteLine($"Restored current cloud save for {game.Name}");
                Console.WriteLine($"Cloud root: {result.RootKey}");
                Console.WriteLine($"Objects: {result.DownloadedObjectCount}");
                Console.WriteLine($"Bytes: {result.DownloadedByteCount}");
                return 0;
            }

            PrintUsage();
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = arg[2..];
            var value = i + 1 < args.Length ? args[++i] : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private static GameSnapshot ResolveGame(AppSnapshot snapshot, string gameName)
    {
        if (string.IsNullOrWhiteSpace(gameName))
        {
            throw new InvalidOperationException("Missing --game.");
        }

        return snapshot.Games.FirstOrDefault(game => string.Equals(game.Name, gameName, StringComparison.Ordinal))
               ?? throw new InvalidOperationException($"Game not found: {gameName}");
    }

    private static void EnsureCloudConfigured(CloudSettings cloudSettings)
    {
        if (!string.Equals(cloudSettings.Backend.Type, "S3", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(cloudSettings.Backend.Endpoint) ||
            string.IsNullOrWhiteSpace(cloudSettings.Backend.Bucket) ||
            string.IsNullOrWhiteSpace(cloudSettings.Backend.AccessKeyId) ||
            string.IsNullOrWhiteSpace(cloudSettings.Backend.SecretAccessKey))
        {
            throw new InvalidOperationException("Cloud backend configuration is incomplete.");
        }
    }

    private static string FormatSize(long size)
    {
        if (size < 1024)
        {
            return $"{size} B";
        }

        if (size < 1024 * 1024)
        {
            return $"{size / 1024d:0.0} KB";
        }

        return $"{size / 1024d / 1024d:0.0} MB";
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  GameSaveManager.CloudTool.exe --action init-backup-dirs --config GameSaveManager.config.json");
        Console.Error.WriteLine("  GameSaveManager.CloudTool.exe --action status --config GameSaveManager.config.json --game \"Game Name\"");
        Console.Error.WriteLine("  GameSaveManager.CloudTool.exe --action upload-current --config GameSaveManager.config.json --game \"Game Name\"");
        Console.Error.WriteLine("  GameSaveManager.CloudTool.exe --action restore-current --config GameSaveManager.config.json --game \"Game Name\"");
    }

    private static (int CreatedFolders, int SkippedFolders, int CreatedBackupsJson, int SkippedBackupsJson) InitializeBackupFolders(
        IEnumerable<GameDefinition> games,
        string backupRoot)
    {
        Directory.CreateDirectory(backupRoot);

        var createdFolders = 0;
        var skippedFolders = 0;
        var createdBackupsJson = 0;
        var skippedBackupsJson = 0;

        foreach (var game in games)
        {
            if (string.IsNullOrWhiteSpace(game.Name))
            {
                continue;
            }

            var gameDirectory = Path.Combine(backupRoot, game.Name);
            if (Directory.Exists(gameDirectory))
            {
                skippedFolders++;
            }
            else
            {
                Directory.CreateDirectory(gameDirectory);
                createdFolders++;
            }

            var backupsJsonPath = Path.Combine(gameDirectory, "Backups.json");
            if (File.Exists(backupsJsonPath))
            {
                skippedBackupsJson++;
                continue;
            }

            var backupManifest = new Dictionary<string, object?>
            {
                ["name"] = game.Name,
                ["backups"] = Array.Empty<object>(),
                ["device_heads"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                ["sync_version"] = 0
            };

            File.WriteAllText(backupsJsonPath, JsonSerializer.Serialize(backupManifest, BackupsJsonOptions));
            createdBackupsJson++;
        }

        return (createdFolders, skippedFolders, createdBackupsJson, skippedBackupsJson);
    }
}
