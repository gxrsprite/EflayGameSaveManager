using EflayGameSaveManager.Core.Models;

namespace EflayGameSaveManager.Core.Services;

/// <summary>
/// Display-name rules shared by all frontends.
/// <see cref="GameDefinition.Name"/> is the stable storage/lookup key and must never
/// be replaced by a localized name. The Chinese name is display-only:
/// when the UI language is Chinese and a Chinese name exists, show it; otherwise fall back to <see cref="GameDefinition.Name"/>.
/// </summary>
public static class GameDisplayNames
{
    public static bool IsChinese(string? language) =>
        string.Equals(language, AppLanguage.Chinese, StringComparison.OrdinalIgnoreCase);

    public static string Resolve(string name, string? chineseName, string? language) =>
        IsChinese(language) && !string.IsNullOrWhiteSpace(chineseName)
            ? chineseName.Trim()
            : name;

    public static string Resolve(GameDefinition game, string? language) =>
        Resolve(game.Name, game.ChineseName, language);

    public static string Resolve(GameSnapshot game, string? language) =>
        Resolve(game.Name, game.ChineseName, language);

    public static bool HasNote(string? note) => !string.IsNullOrWhiteSpace(note);
}
