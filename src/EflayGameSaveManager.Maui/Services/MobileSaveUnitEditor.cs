using EflayGameSaveManager.Core.Models;

namespace EflayGameSaveManager.Maui.Services;

/// <summary>Edits this device's mode without changing another device's shared unit type.</summary>
public static class MobileSaveUnitEditor
{
    public static SaveUnitDefinition Apply(GameDefinition game, string deviceId, int? selectedId,
        SaveUnitType type, string path, List<string> exclude, bool contentsOnly)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Save path is required.");
        var selected = game.SavePaths.FirstOrDefault(unit => unit.Id == selectedId);
        var target = selected;
        if (target is null || target.UnitType != type)
        {
            target = game.SavePaths.FirstOrDefault(unit => unit.UnitType == type &&
                (selected is null || unit.LinkedUnitIds.Contains(selected.Id)));
            if (target is null)
            {
                var id = Math.Max(game.NextSaveUnitId, game.SavePaths.Select(unit => unit.Id + 1).DefaultIfEmpty().Max());
                target = new SaveUnitDefinition { Id = id, UnitType = type };
                game.SavePaths.Add(target);
                game.NextSaveUnitId = id + 1;
            }
            if (selected is not null)
            {
                selected.Paths.Remove(deviceId);
                if (!selected.LinkedUnitIds.Contains(target.Id)) selected.LinkedUnitIds.Add(target.Id);
                if (!target.LinkedUnitIds.Contains(selected.Id)) target.LinkedUnitIds.Add(selected.Id);
            }
        }
        target.Paths[deviceId] = path.Trim();
        target.Exclude = type == SaveUnitType.Zip ? [] : exclude;
        target.ContentsOnly = type is SaveUnitType.Folder or SaveUnitType.Zip && contentsOnly;
        return target;
    }

    public static SaveUnitDefinition? FindZip(GameDefinition? game, string deviceId) =>
        game?.SavePaths.FirstOrDefault(unit => unit.UnitType == SaveUnitType.Zip && unit.Paths.ContainsKey(deviceId));
}
