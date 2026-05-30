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
        if (NormalizeConfig(config))
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

    private static bool NormalizeConfig(ManagerConfig config)
    {
        var changed = false;

        foreach (var game in config.Games)
        {
            var gameChanged = false;
            var mergedUnits = new List<SaveUnitDefinition>();
            foreach (var saveUnit in game.SavePaths.OrderBy(unit => unit.Id))
            {
                var compatibleUnit = mergedUnits.FirstOrDefault(existing =>
                    existing.UnitType == saveUnit.UnitType &&
                    existing.DeleteBeforeApply == saveUnit.DeleteBeforeApply &&
                    !existing.Paths.Keys.Any(deviceId => saveUnit.Paths.ContainsKey(deviceId)));

                if (compatibleUnit is null)
                {
                    mergedUnits.Add(CloneSaveUnit(saveUnit));
                    continue;
                }

                foreach (var pair in saveUnit.Paths)
                {
                    compatibleUnit.Paths[pair.Key] = pair.Value;
                }

                gameChanged = true;
            }

            if (gameChanged)
            {
                for (var index = 0; index < mergedUnits.Count; index++)
                {
                    mergedUnits[index].Id = index;
                }

                game.SavePaths = mergedUnits;
                game.NextSaveUnitId = mergedUnits.Count;
                changed = true;
            }
        }

        return changed;
    }

    private static SaveUnitDefinition CloneSaveUnit(SaveUnitDefinition saveUnit)
    {
        return new SaveUnitDefinition
        {
            Id = saveUnit.Id,
            UnitType = saveUnit.UnitType,
            DeleteBeforeApply = saveUnit.DeleteBeforeApply,
            Paths = new Dictionary<string, string>(saveUnit.Paths, StringComparer.OrdinalIgnoreCase)
        };
    }
}
