using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services.GamePlugins;

namespace EflayGameSaveManager.Maui.Services;

/// <summary>Pure configuration migration shared with desktop-hosted regression tests.</summary>
public static class MobileConfigNormalizer
{
    public static bool Normalize(ManagerConfig config)
    {
        var changed = false;
        foreach (var game in config.Games)
        {
            // CotL uses saves/ on PC and user/ on Android. Its portable profile is
            // always contents-only; repair configurations damaged by the old clone.
            var isLamb = new CultOfTheLambSavePlugin().Matches(new GameSnapshot(
                game.Name, game.CloudSyncEnabled, [], [], [], new CurrentDeviceGamePathInfo(""), game.ChineseName));
            if (isLamb)
            {
                foreach (var unit in game.SavePaths.Where(unit => unit.UnitType == SaveUnitType.Folder))
                {
                    if (!unit.ContentsOnly)
                    {
                        unit.ContentsOnly = true;
                        changed = true;
                    }
                    foreach (var pattern in new[] { "settings*", "Prefs.dict" })
                    {
                        if (!unit.Exclude.Contains(pattern, StringComparer.OrdinalIgnoreCase))
                        {
                            unit.Exclude.Add(pattern);
                            changed = true;
                        }
                    }
                }
            }

            // Linked unit identities must remain intact; do not collapse them.
            if (game.SavePaths.Any(unit => unit.LinkedUnitIds.Count > 0)) continue;
            var merged = new List<SaveUnitDefinition>();
            var didMerge = false;
            foreach (var unit in game.SavePaths.OrderBy(unit => unit.Id))
            {
                var compatible = merged.FirstOrDefault(existing =>
                    existing.UnitType == unit.UnitType &&
                    existing.DeleteBeforeApply == unit.DeleteBeforeApply &&
                    existing.ContentsOnly == unit.ContentsOnly &&
                    existing.Exclude.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(unit.Exclude) &&
                    !existing.Paths.Keys.Any(unit.Paths.ContainsKey));
                if (compatible is null)
                {
                    merged.Add(new SaveUnitDefinition
                    {
                        Id = unit.Id,
                        UnitType = unit.UnitType,
                        DeleteBeforeApply = unit.DeleteBeforeApply,
                        ContentsOnly = unit.ContentsOnly,
                        Exclude = [.. unit.Exclude],
                        LinkedUnitIds = [.. unit.LinkedUnitIds],
                        Paths = new Dictionary<string, string>(unit.Paths, StringComparer.OrdinalIgnoreCase)
                    });
                    continue;
                }
                foreach (var pair in unit.Paths) compatible.Paths[pair.Key] = pair.Value;
                didMerge = true;
            }
            if (didMerge)
            {
                game.SavePaths = merged;
                // Retain stable IDs already used by cloud archives.
                game.NextSaveUnitId = Math.Max(game.NextSaveUnitId, merged.Max(unit => unit.Id) + 1);
                changed = true;
            }
        }
        return changed;
    }
}
