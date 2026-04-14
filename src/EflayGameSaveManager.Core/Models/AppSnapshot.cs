namespace EflayGameSaveManager.Core.Models;

public sealed record AppSnapshot(
    string ConfigPath,
    string BackupRoot,
    IReadOnlyList<GameSnapshot> Games,
    IReadOnlyDictionary<string, DeviceDefinition> Devices,
    CurrentDeviceContext CurrentDevice);

public sealed record GameSnapshot(
    string Name,
    bool CloudSyncEnabled,
    IReadOnlyList<ResolvedSaveUnit> SaveUnits,
    IReadOnlyList<ResolvedGamePath> GamePaths,
    IReadOnlyList<CurrentDevicePathInfo> CurrentDevicePaths,
    CurrentDeviceGamePathInfo CurrentDeviceGamePath);

public sealed record ResolvedSaveUnit(
    int Id,
    SaveUnitType UnitType,
    bool DeleteBeforeApply,
    IReadOnlyList<ResolvedDevicePath> Paths);

public sealed record ResolvedGamePath(string DeviceId, string DeviceName, string Path);

public sealed record ResolvedDevicePath(string DeviceId, string DeviceName, string Path);
