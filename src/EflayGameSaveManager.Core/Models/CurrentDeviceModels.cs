namespace EflayGameSaveManager.Core.Models;

/// <summary>
/// The device the current process is running on. <paramref name="Platform"/> is the real
/// platform reported by the client (<see cref="EflayGameSaveManager.Core.Services.AppPlatform"/>)
/// and is the authoritative input for save-format decisions.
/// <paramref name="PlatformChanged"/> is true when the stored value was just updated, so the
/// caller can persist the config.
/// </summary>
public sealed record CurrentDeviceContext(
    string DeviceId,
    string DeviceName,
    bool WasAdded,
    string Platform = "",
    bool PlatformChanged = false);

public sealed record CurrentDevicePathInfo(
    int SaveUnitId,
    SaveUnitType UnitType,
    string Path,
    bool DeleteBeforeApply);

public sealed record CurrentDeviceGamePathInfo(string Path);
