using System.Diagnostics;

namespace EflayGameSaveManager.Maui.Platforms.Android;

/// <summary>
/// Provides elevated file access via Shizuku for restricted paths
/// (e.g. /data/data/other.app/). Uses ShizukuInterop.RunShellCommand()
/// to execute cp/mkdir/rm with ADB/root privileges.
/// </summary>
public static class ShizukuFileService
{
    public static string StatusMessage
    {
        get
        {
            if (!ShizukuInterop.IsAvailable)
                return "Shizuku: not installed";
            if (!ShizukuInterop.HasPermission)
                return "Shizuku: no permission (open Shizuku app to authorize)";
            if (ShizukuInterop.IsRoot)
                return "Shizuku: ready (root — full /data/data access)";
            return "Shizuku: ready (ADB — /data/data may be restricted)";
        }
    }

    /// <summary>
    /// Copies a restricted file/directory to an accessible temp location via Shizuku.
    /// Returns the temp directory path on success, null on failure.
    /// </summary>
    public static async Task<string?> CopyFromRestrictedAsync(string restrictedPath, string destDir)
    {
        if (!ShizukuInterop.IsAvailable || !ShizukuInterop.HasPermission)
            return null;
        if (!IsRestrictedPath(restrictedPath))
            return null;

        Directory.CreateDirectory(destDir);
        var destName = Path.GetFileName(restrictedPath.TrimEnd('/'));
        if (string.IsNullOrWhiteSpace(destName)) destName = "copy";
        // Keep the original name for folder-inclusive mode. Contents-only mode is
        // archived under the stable "content" wrapper by the Core archive service.
        var tempDest = Path.Combine(destDir, destName);

        // Clean stale copy
        if (Directory.Exists(tempDest)) Directory.Delete(tempDest, true);
        if (File.Exists(tempDest)) File.Delete(tempDest);

        // NOTE: in-app Directory.Exists always returns false for restricted paths
        // (EACCES), so directory detection must go through the elevated shell.
        // toybox `cp -T` rejects an existing destination directory, so copy the
        // source contents explicitly ("src/." -> "dest/").
        var isDir = restrictedPath.EndsWith('/') || await IsDirectoryViaShellAsync(restrictedPath);
        var cmd = isDir
            ? $"mkdir -p '{EscapeShell(tempDest)}' && cp -r --preserve=timestamps '{EscapeShell(restrictedPath)}'/. '{EscapeShell(tempDest)}'/"
            : $"cp --preserve=timestamps '{EscapeShell(restrictedPath)}' '{EscapeShell(tempDest)}'";
        // A root-owned source directory can be mode 0700. cp retains that mode even
        // without --preserve=mode, leaving the app unable to traverse its own staged
        // copy. Only relax this disposable copy inside the app's private cache.
        cmd += $" && chmod -R a+rwX '{EscapeShell(tempDest)}'";

        Debug.WriteLine($"[Shizuku] copyFromRestricted: {cmd}");

        return await Task.Run(() =>
        {
            var (exitCode, stdout, stderr) = ShizukuInterop.RunShellCommand(cmd);
            if (exitCode != 0)
            {
                Debug.WriteLine($"[Shizuku] copyFromRestricted failed (exit={exitCode}): {stderr}");
                return null;
            }
            return Directory.Exists(tempDest) || File.Exists(tempDest) ? tempDest : null;
        });
    }

    /// <summary>
    /// Copies accessible content into a restricted path via Shizuku.
    /// </summary>
    public static async Task<bool> CopyToRestrictedAsync(string sourcePath, string restrictedPath)
    {
        if (!ShizukuInterop.IsAvailable || !ShizukuInterop.HasPermission)
            return false;
        if (!IsRestrictedPath(restrictedPath))
            return false;

        // Ensure parent exists
        var parent = Path.GetDirectoryName(restrictedPath.TrimEnd('/'));
        if (!string.IsNullOrWhiteSpace(parent))
        {
            await Task.Run(() =>
                ShizukuInterop.RunShellCommand($"mkdir -p '{EscapeShell(parent)}'"));
        }

        // toybox `cp -T` fails when DEST already exists as a directory (always the case
        // on restore), so merge contents with "src/." -> "dest/".
        var cmd = Directory.Exists(sourcePath)
            ? $"mkdir -p '{EscapeShell(restrictedPath)}' && cp -r --preserve=timestamps '{EscapeShell(sourcePath)}'/. '{EscapeShell(restrictedPath)}'/"
            : $"cp --preserve=timestamps '{EscapeShell(sourcePath)}' '{EscapeShell(restrictedPath)}'";

        Debug.WriteLine($"[Shizuku] copyToRestricted: {cmd}");

        return await Task.Run(() =>
        {
            var (exitCode, _, stderr) = ShizukuInterop.RunShellCommand(cmd);
            if (exitCode != 0)
            {
                Debug.WriteLine($"[Shizuku] copyToRestricted failed: {stderr}");
                return false;
            }
            return true;
        });
    }

    /// <summary>Retain an exact duplicate left by the old folder-wrapper restore bug.</summary>
    public static async Task<string?> ArchiveDuplicateRestoreWrapperAsync(string stagedDirectory, string targetDirectory)
    {
        if (!Directory.Exists(stagedDirectory) || !Directory.EnumerateFiles(stagedDirectory, "*", SearchOption.AllDirectories).Any())
            return null;
        var name = Path.GetFileName(targetDirectory.TrimEnd('/'));
        if (string.IsNullOrWhiteSpace(name) || !IsRestrictedPath(targetDirectory)) return null;
        var nested = targetDirectory.TrimEnd('/') + "/" + name;
        if (!await IsDirectoryViaShellAsync(nested)) return null;
        var backupRoot = Path.Combine(FileSystem.AppDataDirectory, "restore-layout-backups", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupRoot);
        var backup = Path.Combine(backupRoot, name);
        // diff checks all file bytes and directory entries; differing/newer saves stay
        // untouched. Rename into private persistent storage instead of deleting data.
        var command = $"test ! -L '{EscapeShell(nested)}' && diff -qr '{EscapeShell(stagedDirectory)}' '{EscapeShell(nested)}' >/dev/null 2>&1 && mv '{EscapeShell(nested)}' '{EscapeShell(backup)}'";
        var result = await Task.Run(() => ShizukuInterop.RunShellCommand(command));
        if (result.ExitCode != 0) return null;
        await Task.Run(() => ShizukuInterop.RunShellCommand($"chmod -R a+rwX '{EscapeShell(backup)}'"));
        await File.WriteAllTextAsync(Path.Combine(backupRoot, "RESTORE.txt"),
            $"Duplicate folder retained after restore.\nOriginal: {nested}\nBackup: {backup}\n");
        return backup;
    }

    public static bool IsRestrictedPath(string path)
    {
        return path.StartsWith("/data/data/") ||
               path.StartsWith("/data/user/") ||
               path.StartsWith("/data/media/obb/") ||
               path.StartsWith("/sdcard/Android/data/") ||
               path.StartsWith("/storage/emulated/0/Android/data/");
    }

    private static async Task<bool> IsDirectoryViaShellAsync(string restrictedPath)
    {
        if (!ShizukuInterop.IsAvailable || !ShizukuInterop.HasPermission)
            return Directory.Exists(restrictedPath);

        var result = await Task.Run(() =>
            ShizukuInterop.RunShellCommand($"test -d '{EscapeShell(restrictedPath)}'"));
        return result.ExitCode == 0;
    }

    private static string EscapeShell(string path)
    {
        return path.Replace("'", "'\\''");
    }
}
