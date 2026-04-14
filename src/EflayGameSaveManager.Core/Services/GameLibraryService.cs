using EflayGameSaveManager.Core.Models;

namespace EflayGameSaveManager.Core.Services;

public sealed class GameLibraryService
{
    private readonly EnvironmentTokenResolver _tokenResolver;
    private readonly CurrentDeviceService _currentDeviceService;
    private readonly AppRuntimeSettingsService _runtimeSettingsService;

    public GameLibraryService(
        EnvironmentTokenResolver tokenResolver,
        CurrentDeviceService currentDeviceService,
        AppRuntimeSettingsService? runtimeSettingsService = null)
    {
        _tokenResolver = tokenResolver;
        _currentDeviceService = currentDeviceService;
        _runtimeSettingsService = runtimeSettingsService ?? new AppRuntimeSettingsService();
    }

    public AppSnapshot CreateSnapshot(ManagerConfig config, string configPath, string? machineName = null)
    {
        var configDirectory = Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory;
        var backupRoot = ResolveBackupRoot(configDirectory, config.BackupPath);
        var runtimeSettings = _runtimeSettingsService.Load(configPath);
        var resolvedDeviceName = string.IsNullOrWhiteSpace(runtimeSettings.ForcedDeviceName)
            ? machineName
            : runtimeSettings.ForcedDeviceName.Trim();
        var currentDevice = _currentDeviceService.EnsureCurrentDevice(config, resolvedDeviceName);

        var games = config.Games
            .Select(game =>
            {
                var currentDeviceGamePath = _currentDeviceService.GetCurrentDeviceGamePath(game, currentDevice.DeviceId);
                return new GameSnapshot(
                    game.Name,
                    game.CloudSyncEnabled,
                    game.SavePaths.Select(saveUnit => new ResolvedSaveUnit(
                        saveUnit.Id,
                        saveUnit.UnitType,
                        saveUnit.DeleteBeforeApply,
                        GetResolvedSaveUnitPaths(config, saveUnit, currentDevice)
                            .Select(path => new ResolvedDevicePath(
                                path.DeviceId,
                                ResolveDeviceName(config.Devices, path.DeviceId),
                                _tokenResolver.ResolvePath(path.Path)))
                            .OrderBy(path => path.DeviceName, StringComparer.OrdinalIgnoreCase)
                            .ToArray()))
                        .ToArray(),
                    game.GamePaths
                        .Select(path => new ResolvedGamePath(
                            path.Key,
                            ResolveDeviceName(config.Devices, path.Key),
                            _tokenResolver.ResolvePath(path.Value)))
                        .OrderBy(path => path.DeviceName, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    _currentDeviceService.GetCurrentDevicePaths(game, currentDevice.DeviceId)
                        .Select(path => path with { Path = _tokenResolver.ResolvePath(path.Path) })
                        .ToArray(),
                    currentDeviceGamePath with
                    {
                        Path = _tokenResolver.ResolvePath(currentDeviceGamePath.Path)
                    });
            })
            .OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new AppSnapshot(configPath, backupRoot, games, config.Devices, currentDevice);
    }

    private IReadOnlyList<(string DeviceId, string Path)> GetResolvedSaveUnitPaths(
        ManagerConfig config,
        SaveUnitDefinition saveUnit,
        CurrentDeviceContext currentDevice)
    {
        var paths = saveUnit.Paths
            .Select(path => (path.Key, path.Value))
            .ToList();

        if (!saveUnit.Paths.ContainsKey(currentDevice.DeviceId))
        {
            paths.Add((currentDevice.DeviceId, _currentDeviceService.GetEffectiveCurrentDevicePath(saveUnit, currentDevice.DeviceId)));
        }

        return paths;
    }

    public string ResolveBackupRoot(string configDirectory, string backupPath)
    {
        var expanded = _tokenResolver.ResolvePath(backupPath);
        return Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(Path.Combine(configDirectory, expanded));
    }

    private static string ResolveDeviceName(IReadOnlyDictionary<string, DeviceDefinition> devices, string deviceId)
    {
        return devices.TryGetValue(deviceId, out var device)
            ? device.Name
            : deviceId;
    }
}
