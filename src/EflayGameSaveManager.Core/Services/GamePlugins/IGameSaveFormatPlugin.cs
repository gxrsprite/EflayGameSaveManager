using EflayGameSaveManager.Core.Models;

namespace EflayGameSaveManager.Core.Services.GamePlugins;

/// <summary>
/// Target platform of a game save file. Different builds of the same game
/// (e.g. Steam PC vs Android port) may use incompatible save encodings.
/// </summary>
public enum GameSaveTargetPlatform
{
    Unknown,
    /// <summary>Steam PC / legacy JSON+AES era (game ≤ 1.4.x).</summary>
    SteamPc,
    /// <summary>Steam PC 1.5.x+: slot saves are MessagePack <c>.mp</c>; JSON slots are ignored.</summary>
    SteamPcMessagePack,
    AndroidPort
}

/// <summary>
/// Per-game save-format adapter. Backup normalizes every save file into a
/// canonical form (plain UTF-8 JSON, extensionless entry name); restore
/// re-encodes from canonical into whatever the target platform expects.
/// Files the plugin does not recognize are passed through untouched.
/// </summary>
public interface IGameSaveFormatPlugin
{
    bool Matches(GameSnapshot game);

    GameSaveTargetPlatform ResolveTarget(GameSnapshot game, CurrentDeviceContext device);

    /// <summary>
    /// Tries to convert a raw save file into canonical form.
    /// Returns false when the file is not handled by this plugin.
    /// </summary>
    bool TryToCanonical(string entryName, byte[] raw, out string canonicalName, out byte[] canonicalBytes);

    byte[] FromCanonical(byte[] canonicalJson, GameSaveTargetPlatform target);

    string ToTargetName(string canonicalName, GameSaveTargetPlatform target);
}
