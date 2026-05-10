using System.Text.Json;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Serialization;

namespace EflayGameSaveManager.Core.Services;

public sealed class GameSaveManagerConfigurationService
{
    public const string ConfigFileName = "GameSaveManager.config.json";

    public string GetDefaultConfigPath(string? startDirectory = null)
    {
        return Path.Combine(startDirectory ?? AppContext.BaseDirectory, ConfigFileName);
    }

    public string FindConfigPath(string? startDirectory = null)
    {
        var directory = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, ConfigFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {ConfigFileName} from the current application directory.");
    }

    public ManagerConfig CreateDefault()
    {
        return new ManagerConfig
        {
            Version = "1.0.0",
            BackupPath = "./save_data",
            Games = [],
            Devices = [],
            Settings = new AppSettings
            {
                Locale = "zh_SIMPLIFIED",
                LogToFile = true,
                CloudSettings = new CloudSettings
                {
                    MaxConcurrency = 1
                }
            },
            Favorites = [],
            QuickAction = new QuickActionSettings
            {
                QuickActionGame = JsonDocument.Parse("null").RootElement.Clone(),
                Hotkeys = new HotkeySettings
                {
                    Apply = ["", "", ""],
                    Backup = ["", "", ""]
                }
            }
        };
    }

    public async Task<ManagerConfig> LoadAsync(string configPath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(configPath);
        var config = await JsonSerializer.DeserializeAsync(stream, ManagerJsonSerializerContext.Default.ManagerConfig, cancellationToken);

        return config as ManagerConfig ?? throw new InvalidOperationException($"Failed to deserialize configuration file: {configPath}");
    }

    public async Task SaveAsync(string configPath, ManagerConfig config, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = configPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, config, ManagerJsonSerializerContext.Default.ManagerConfig, cancellationToken);
        }

        File.Move(tempPath, configPath, overwrite: true);
    }
}
