using EflayGameSaveManager.Core.Models;

namespace EflayGameSaveManager.Core.Services;

public sealed class CurrentDeviceService
{
    public CurrentDeviceContext EnsureCurrentDevice(ManagerConfig config, string? machineName = null)
    {
        var currentMachineName = string.IsNullOrWhiteSpace(machineName) ? Environment.MachineName : machineName.Trim();

        foreach (var device in config.Devices.Values)
        {
            if (string.Equals(device.Name, currentMachineName, StringComparison.OrdinalIgnoreCase))
            {
                return new CurrentDeviceContext(device.Id, device.Name, false);
            }
        }

        var newId = Guid.NewGuid().ToString();
        config.Devices[newId] = new DeviceDefinition
        {
            Id = newId,
            Name = currentMachineName
        };

        return new CurrentDeviceContext(newId, currentMachineName, true);
    }
    public IReadOnlyList<CurrentDevicePathInfo> GetCurrentDevicePaths(GameDefinition game, string deviceId)
    {
        return game.SavePaths
            .Select(unit => new CurrentDevicePathInfo(
                unit.Id,
                unit.UnitType,
                GetEffectiveCurrentDevicePath(unit, deviceId),
                unit.DeleteBeforeApply))
            .ToArray();
    }

    public CurrentDeviceGamePathInfo GetCurrentDeviceGamePath(GameDefinition game, string deviceId)
    {
        return new CurrentDeviceGamePathInfo(GetEffectiveCurrentGamePath(game, deviceId));
    }

    public string GetEffectiveCurrentDevicePath(SaveUnitDefinition saveUnit, string deviceId)
    {
        return saveUnit.Paths.TryGetValue(deviceId, out var value)
            ? value
            : saveUnit.Paths.Values.FirstOrDefault() ?? string.Empty;
    }

    public string GetEffectiveCurrentGamePath(GameDefinition game, string deviceId)
    {
        return game.GamePaths.TryGetValue(deviceId, out var value)
            ? value
            : game.GamePaths.Values.FirstOrDefault() ?? string.Empty;
    }

    public void UpdateCurrentDeviceSavePath(GameDefinition game, string deviceId, int saveUnitId, string path)
    {
        var saveUnit = game.SavePaths.FirstOrDefault(unit => unit.Id == saveUnitId)
                       ?? throw new InvalidOperationException($"Save unit not found: {saveUnitId}");
        saveUnit.Paths[deviceId] = path.Trim();
    }

    public void UpdateCurrentDeviceGamePath(GameDefinition game, string deviceId, string path)
    {
        game.GamePaths[deviceId] = path.Trim();
    }
}
