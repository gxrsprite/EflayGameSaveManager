using EflayGameSaveManager.Core.Models;

namespace EflayGameSaveManager.Maui.Services;

public static class AndroidGameSnapshotFactory
{
    /// <summary>Keep the logical folder name while isolating restricted-path restores.</summary>
    public static string GetRestoreStagingPath(string stagingRoot, int unitId, SaveUnitType type, string originalPath)
    {
        var unitRoot = Path.Combine(stagingRoot, $"unit-{unitId}");
        if (type != SaveUnitType.Folder) return unitRoot;
        var folderName = Path.GetFileName(originalPath.TrimEnd('/', '\\'));
        if (string.IsNullOrWhiteSpace(folderName))
            throw new InvalidOperationException("受限存档目录必须指向一个具体文件夹。");
        // Folder-inclusive archives contain e.g. 0/savedata/*. The Core restore
        // compares that wrapper with the destination basename before copying it.
        // Redirecting to unit-0 alone loses the name and produces savedata/savedata.
        return Path.Combine(unitRoot, folderName);
    }

    public static GameSnapshot Create(
        GameSnapshot source,
        GameDefinition gameDefinition,
        string currentDeviceId)
    {
        var explicitSaveUnits = source.SaveUnits
            .Where(unit => gameDefinition.SavePaths.Any(definition =>
                definition.Id == unit.Id &&
                definition.Paths.TryGetValue(currentDeviceId, out var configuredPath) &&
                !string.IsNullOrWhiteSpace(configuredPath)))
            .Select(unit =>
            {
                var explicitPath = gameDefinition.SavePaths
                    .Where(definition => definition.Id == unit.Id)
                    .SelectMany(definition => definition.Paths)
                    .Where(path => string.Equals(path.Key, currentDeviceId, StringComparison.OrdinalIgnoreCase))
                    .Select(path => path.Value)
                    .FirstOrDefault() ?? string.Empty;

                return unit with
                {
                    Paths =
                    [
                        new ResolvedDevicePath(
                            currentDeviceId,
                            unit.Paths.FirstOrDefault(path => string.Equals(path.DeviceId, currentDeviceId, StringComparison.OrdinalIgnoreCase))?.DeviceName ?? "Android",
                            explicitPath)
                    ]
                };
            })
            .ToArray();

        var explicitCurrentDevicePaths = explicitSaveUnits
            .Select(unit => new CurrentDevicePathInfo(
                unit.Id,
                unit.UnitType,
                unit.Paths[0].Path,
                unit.DeleteBeforeApply))
            .ToArray();

        var explicitGamePath = gameDefinition.GamePaths.TryGetValue(currentDeviceId, out var gamePath)
            ? gamePath
            : string.Empty;

        return source with
        {
            SaveUnits = explicitSaveUnits,
            CurrentDevicePaths = explicitCurrentDevicePaths,
            CurrentDeviceGamePath = new CurrentDeviceGamePathInfo(explicitGamePath)
        };
    }
}
