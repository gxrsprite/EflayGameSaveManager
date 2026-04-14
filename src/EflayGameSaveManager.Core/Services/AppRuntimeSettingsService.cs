using System.Text.Json;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Serialization;

namespace EflayGameSaveManager.Core.Services;

public sealed class AppRuntimeSettingsService
{
    public const string FileName = "GameSaveManager.runtime.json";

    public string GetSettingsPath(string configPath)
    {
        var directory = Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory;
        return Path.Combine(directory, FileName);
    }

    public AppRuntimeSettings Load(string configPath)
    {
        var settingsPath = GetSettingsPath(configPath);
        if (!File.Exists(settingsPath))
        {
            return new AppRuntimeSettings();
        }

        var json = File.ReadAllText(settingsPath);
        return JsonSerializer.Deserialize(json, ManagerJsonSerializerContext.Default.AppRuntimeSettings) ?? new AppRuntimeSettings();
    }
}
