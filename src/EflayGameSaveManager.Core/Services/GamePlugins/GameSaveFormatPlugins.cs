using EflayGameSaveManager.Core.Models;

namespace EflayGameSaveManager.Core.Services.GamePlugins;

/// <summary>Registry of per-game save-format plugins, checked in order.</summary>
public static class GameSaveFormatPlugins
{
    private static readonly IReadOnlyList<IGameSaveFormatPlugin> All =
    [
        new CultOfTheLambSavePlugin()
    ];

    public static IGameSaveFormatPlugin? ForGame(GameSnapshot game)
    {
        return All.FirstOrDefault(plugin => plugin.Matches(game));
    }

    /// <summary>
    /// Backup hook: rewrites every recognized save file under <paramref name="stagingRoot"/>
    /// into canonical form (content + extensionless name), in place.
    /// </summary>
    public static void NormalizeStaging(IGameSaveFormatPlugin plugin, string stagingRoot)
    {
        foreach (var file in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var raw = File.ReadAllBytes(file);
            if (!plugin.TryToCanonical(Path.GetFileName(file), raw, out var canonicalName, out var canonicalBytes))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(file)!;
            var canonicalPath = Path.Combine(directory, canonicalName);
            File.WriteAllBytes(file, canonicalBytes);
            if (!string.Equals(file, canonicalPath, StringComparison.Ordinal))
            {
                if (File.Exists(canonicalPath))
                {
                    File.Delete(canonicalPath);
                }

                File.Move(file, canonicalPath);
            }
        }
    }

    /// <summary>
    /// Restore hook: converts every canonical save file under <paramref name="extractRoot"/>
    /// into the target platform encoding (content + name), in place.
    /// Files already in target form or unknown files are left untouched.
    /// </summary>
    public static void EncodeStaging(
        IGameSaveFormatPlugin plugin,
        string extractRoot,
        GameSnapshot game,
        CurrentDeviceContext device,
        GameSaveTargetPlatform? targetOverride = null)
    {
        var target = targetOverride ?? plugin.ResolveTarget(game, device);
        if (target == GameSaveTargetPlatform.Unknown)
        {
            return;
        }

        if (target == GameSaveTargetPlatform.SteamPcMessagePack)
        {
            // Do not silently write JSON slot files the game will ignore.
            throw new InvalidOperationException(
                $"'{game.Name}' 的目标平台使用 MessagePack 槽位存档（*.mp，游戏 1.5.x 起）。" +
                "当前插件只能输出 JSON 槽位，游戏会忽略它，因此已中止恢复以免造成假成功。" +
                "请在游戏内新建存档，或使用 1.4.x 版本客户端读取旧存档。");
        }

        foreach (var file in Directory.EnumerateFiles(extractRoot, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var raw = File.ReadAllBytes(file);
            var fileName = Path.GetFileName(file);

            // Already in target form? leave it.
            if (IsTargetForm(plugin, target, fileName, raw))
            {
                continue;
            }

            if (!plugin.TryToCanonical(fileName, raw, out var canonicalName, out var canonicalBytes))
            {
                continue;
            }

            var encoded = plugin.FromCanonical(canonicalBytes, target);
            var targetName = plugin.ToTargetName(canonicalName, target);
            var directory = Path.GetDirectoryName(file)!;
            var targetPath = Path.Combine(directory, targetName);
            File.WriteAllBytes(file, encoded);
            if (!string.Equals(file, targetPath, StringComparison.Ordinal))
            {
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                File.Move(file, targetPath);
            }
        }
    }

    private static bool IsTargetForm(
        IGameSaveFormatPlugin plugin,
        GameSaveTargetPlatform target,
        string fileName,
        byte[] raw)
    {
        if (raw.Length == 0)
        {
            return true;
        }

        if (!plugin.TryToCanonical(fileName, raw, out var canonicalName, out var canonicalBytes))
        {
            return true; // unknown file: passthrough counts as "already fine"
        }

        // Round-trip check: canonical -> target must reproduce the raw bytes
        // modulo the random key/IV (Steam) or gzip level (port). Compare shapes instead:
        var expectedName = plugin.ToTargetName(canonicalName, target);
        if (!string.Equals(fileName, expectedName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (target == GameSaveTargetPlatform.SteamPc)
        {
            return raw.Length > 33 && raw[0] == (byte)'E';
        }

        return raw.Length > 2 && raw[0] == (byte)'Z' && raw[1] == (byte)'B';
    }
}
