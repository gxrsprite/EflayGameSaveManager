namespace EflayGameSaveManager.Core.Models;

public sealed record CurrentDeviceContext(string DeviceId, string DeviceName, bool WasAdded);

public sealed record CurrentDevicePathInfo(
    int SaveUnitId,
    SaveUnitType UnitType,
    string Path,
    bool DeleteBeforeApply);

public sealed record CurrentDeviceGamePathInfo(string Path);
