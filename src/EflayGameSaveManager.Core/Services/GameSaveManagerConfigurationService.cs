using System.Text.Json;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Serialization;

namespace EflayGameSaveManager.Core.Services;

public sealed class GameSaveManagerConfigurationService
{
    public string FindConfigPath(string? startDirectory = null)
    {
        var directory = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "GameSaveManager.config.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate GameSaveManager.config.json from the current application directory.");
    }

    public async Task<ManagerConfig> LoadAsync(string configPath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(configPath);
        var config = await JsonSerializer.DeserializeAsync(stream, ManagerJsonSerializerContext.Default.ManagerConfig, cancellationToken);

        return config as ManagerConfig ?? throw new InvalidOperationException($"Failed to deserialize configuration file: {configPath}");
    }

    public async Task SaveAsync(string configPath, ManagerConfig config, CancellationToken cancellationToken = default)
    {
        var tempPath = configPath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, config, ManagerJsonSerializerContext.Default.ManagerConfig, cancellationToken);
        }

        File.Move(tempPath, configPath, overwrite: true);
    }
}
