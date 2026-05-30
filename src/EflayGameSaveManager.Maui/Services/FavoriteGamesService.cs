using System.Text.Json;

namespace EflayGameSaveManager.Maui.Services;

public sealed class FavoriteGamesService
{
    private const string PreferenceKey = "favorite_game_names";

    public IReadOnlySet<string> Load()
    {
        var json = Preferences.Default.Get(PreferenceKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var names = JsonSerializer.Deserialize<List<string>>(json) ?? [];
        return new HashSet<string>(names.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.OrdinalIgnoreCase);
    }

    public void Save(IEnumerable<string> names)
    {
        var normalized = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Preferences.Default.Set(PreferenceKey, JsonSerializer.Serialize(normalized));
    }
}
