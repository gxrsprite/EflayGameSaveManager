using EflayGameSaveManager.Core.Services;
using EflayGameSaveManager.Core.Models;

namespace EflayGameSaveManager.Maui.Services;

public sealed class MobileWorkspaceService
{
    public async Task<string> EnsureConfigPathAsync(GameSaveManagerConfigurationService configurationService, CancellationToken cancellationToken = default)
    {
        var rootDirectory = FileSystem.Current.AppDataDirectory;
        Directory.CreateDirectory(rootDirectory);

        var configPath = Path.Combine(rootDirectory, GameSaveManagerConfigurationService.ConfigFileName);
        if (!File.Exists(configPath))
        {
            var copiedConfig = await TrySeedFromPackageAsync(GameSaveManagerConfigurationService.ConfigFileName, configPath, cancellationToken);
            if (!copiedConfig)
            {
                await configurationService.SaveAsync(configPath, configurationService.CreateDefault(), cancellationToken);
            }
        }

        var runtimePath = Path.Combine(rootDirectory, AppRuntimeSettingsService.FileName);
        if (!File.Exists(runtimePath))
        {
            await TrySeedFromPackageAsync(AppRuntimeSettingsService.FileName, runtimePath, cancellationToken);
        }

        var config = await configurationService.LoadAsync(configPath, cancellationToken);
        if (MobileConfigNormalizer.Normalize(config))
        {
            await configurationService.SaveAsync(configPath, config, cancellationToken);
        }

        return configPath;
    }

    private static async Task<bool> TrySeedFromPackageAsync(string assetName, string destinationPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var source = await FileSystem.Current.OpenAppPackageFileAsync(assetName);
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            await using var destination = File.Create(destinationPath);
            await source.CopyToAsync(destination, cancellationToken);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

}
