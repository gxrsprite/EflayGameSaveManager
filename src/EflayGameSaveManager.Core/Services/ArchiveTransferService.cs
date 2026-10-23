using System.IO.Compression;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services.GamePlugins;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace EflayGameSaveManager.Core.Services;

public sealed class ArchiveTransferService
{
    private const string RgsmArchiveV2Comment = "RGSM_ARCHIVE_V2\n{\"version\":2,\"compression\":\"deflate\"}";
    private const string RegistryExportFileName = "registry.reg";
    private static readonly byte[] EndOfCentralDirectorySignature = [0x50, 0x4b, 0x05, 0x06];
    private readonly WinRegistryTransferService _registryTransferService = new();

    public string CreateCurrentDeviceArchive(
        GameSnapshot game,
        CurrentDeviceContext currentDevice,
        string workingDirectory)
    {
        Directory.CreateDirectory(workingDirectory);
        var stagingDirectory = Path.Combine(workingDirectory, "content");
        Directory.CreateDirectory(stagingDirectory);
        var includedUnits = game.SaveUnits
            .Select(saveUnit => new
            {
                SaveUnit = saveUnit,
                CurrentPath = saveUnit.Paths.FirstOrDefault(path => string.Equals(path.DeviceId, currentDevice.DeviceId, StringComparison.Ordinal))
            })
            .Where(item =>
                item.CurrentPath is not null &&
                !string.IsNullOrWhiteSpace(item.CurrentPath.Path) &&
                CanIncludePath(item.SaveUnit.UnitType, item.CurrentPath.Path))
            .ToArray();

        // Pure Zip mode: all units are Zip type → return the zip file directly
        if (includedUnits.Length > 0 && includedUnits.All(item => item.SaveUnit.UnitType == SaveUnitType.Zip))
        {
            return includedUnits[0].CurrentPath!.Path;
        }

        if (includedUnits.Length == 0)
        {
            var checkedPaths = game.SaveUnits
                .SelectMany(saveUnit => saveUnit.Paths
                    .Where(path => string.Equals(path.DeviceId, currentDevice.DeviceId, StringComparison.Ordinal))
                    .Select(path => $"[{saveUnit.UnitType}] {(string.IsNullOrWhiteSpace(path.Path) ? "<empty>" : path.Path)}"))
                .ToArray();
            var detail = checkedPaths.Length == 0
                ? "no save paths are configured for this device"
                : string.Join("; ", checkedPaths);
            throw new InvalidOperationException(
                $"No save data was found for '{game.Name}' on device '{currentDevice.DeviceName}'. Checked: {detail}. " +
                "Nothing was archived or uploaded — verify the save paths exist and contain files.");
        }

        foreach (var item in includedUnits)
        {
            var saveUnit = item.SaveUnit;
            var currentPath = item.CurrentPath!;

            if (saveUnit.UnitType == SaveUnitType.Zip)
                continue;

            var stagedPath = Path.Combine(stagingDirectory, saveUnit.Id.ToString());
            var exclude = saveUnit.Exclude;

            if (saveUnit.UnitType == SaveUnitType.WinRegistry)
            {
                _registryTransferService.ExportKey(currentPath.Path, Path.Combine(stagedPath, RegistryExportFileName));
            }
            else if (saveUnit.UnitType == SaveUnitType.File)
            {
                var fileName = Path.GetFileName(currentPath.Path);
                if (SavePathFilter.IsExcluded(fileName, fileName, exclude))
                {
                    AppLogger.Info($"Backup skip excluded file: {currentPath.Path}");
                    continue;
                }

                Directory.CreateDirectory(stagedPath);
                FileCopyHelper.CopyFile(currentPath.Path, Path.Combine(stagedPath, fileName));
            }
            else
            {
                if (saveUnit.ContentsOnly)
                {
                    // Stable wrapper name for contents-only units: folder names on the
                    // source/target devices may differ, but archive layout stays "content/*".
                    CopyDirectoryContents(currentPath.Path, Path.Combine(stagedPath, "content"), exclude);
                }
                else
                {
                    CopyDirectoryIncludingBaseDirectory(currentPath.Path, stagedPath, exclude);
                }
            }
        }

        var archivePath = Path.Combine(workingDirectory, "current-device-save.zip");
        if (File.Exists(archivePath))
        {
            FileCopyHelper.DeleteFileIfExists(archivePath);
        }

        // Per-game format plugin: store canonical (portable) saves in the archive.
        var backupPlugin = GameSaveFormatPlugins.ForGame(game);
        if (backupPlugin is not null)
        {
            GameSaveFormatPlugins.NormalizeStaging(backupPlugin, stagingDirectory);
        }

        ZipFile.CreateFromDirectory(stagingDirectory, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            if (archive.Entries.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Archive for '{game.Name}' on device '{currentDevice.DeviceName}' is empty — the save directories exist but contain no files. " +
                    "Nothing was archived or uploaded.");
            }
        }

        WriteZipComment(archivePath, RgsmArchiveV2Comment);
        return archivePath;
    }

    /// <summary>
    /// Restores an archive into the current device's save paths.
    /// <paramref name="targetPlatformOverride"/> lets a caller that temporarily redirects
    /// the destination (e.g. the Android restore path going through a cache folder) pin the
    /// encoding platform resolved from the real device, so the format never degrades to
    /// desktop JSON on a mobile target.
    /// </summary>
    public void RestoreCurrentDeviceArchive(
        string archivePath,
        GameSnapshot game,
        CurrentDeviceContext currentDevice,
        GameSaveTargetPlatform? targetPlatformOverride = null)
    {
        // Zip type: save downloaded zip directly to target path without extraction
        var zipUnits = game.SaveUnits
            .Where(u => u.UnitType == SaveUnitType.Zip)
            .Select(u => new { Unit = u, Path = u.Paths.FirstOrDefault(p => string.Equals(p.DeviceId, currentDevice.DeviceId, StringComparison.Ordinal)) })
            .Where(x => x.Path is not null && !string.IsNullOrWhiteSpace(x.Path.Path))
            .ToArray();

        if (zipUnits.Length > 0)
        {
            var zipWorkRoot = Path.Combine(Path.GetTempPath(), "EflayGameSaveManager", "zip-restore", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(zipWorkRoot);
            try
            {
                var normalizedArchivePath = NormalizeZipArchiveForMobileRestore(archivePath, zipWorkRoot);
                foreach (var item in zipUnits)
                {
                    var targetDir = Path.GetDirectoryName(item.Path!.Path);
                    if (!string.IsNullOrWhiteSpace(targetDir)) Directory.CreateDirectory(targetDir);
                    FileCopyHelper.CopyFile(normalizedArchivePath, item.Path.Path);
                    AppLogger.Info($"Restore Zip unit {item.Unit.Id}: saved to {item.Path.Path}");
                }
            }
            finally
            {
                if (Directory.Exists(zipWorkRoot))
                {
                    Directory.Delete(zipWorkRoot, recursive: true);
                }
            }

            return;
        }

        var extractRoot = Path.Combine(Path.GetTempPath(), "EflayGameSaveManager", "restore", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractRoot);
        try
        {
            ExtractArchive(archivePath, extractRoot);
            AppLogger.Info($"Restore extracted archive to {extractRoot}");

            // Per-game format plugin: convert canonical saves into the target platform encoding.
            var restorePlugin = GameSaveFormatPlugins.ForGame(game);
            if (restorePlugin is not null)
            {
                GameSaveFormatPlugins.EncodeStaging(restorePlugin, extractRoot, game, currentDevice, targetPlatformOverride);
            }

            foreach (var localUnit in game.SaveUnits)
            {
                var localPath = localUnit.Paths.FirstOrDefault(path => string.Equals(path.DeviceId, currentDevice.DeviceId, StringComparison.Ordinal));
                if (localPath is null || string.IsNullOrWhiteSpace(localPath.Path))
                {
                    AppLogger.Info($"Restore skip unit {localUnit.Id}: no path for device {currentDevice.DeviceId}");
                    continue;
                }

                AppLogger.Info($"Restore unit {localUnit.Id} [{localUnit.UnitType}] -> {localPath.Path}");

                var sourceRoot = ResolveSourceRoot(extractRoot, localUnit.Id, game.SaveUnits.Count);
                if (sourceRoot is null)
                {
                    AppLogger.Info($"Restore skip unit {localUnit.Id}: source root not found in archive");
                    continue;
                }

                AppLogger.Info($"Restore unit {localUnit.Id} sourceRoot={sourceRoot}");

                if (localUnit.DeleteBeforeApply)
                {
                    if (localUnit.UnitType == SaveUnitType.WinRegistry)
                    {
                        _registryTransferService.DeleteKey(localPath.Path);
                    }
                    else if (localUnit.UnitType == SaveUnitType.File && File.Exists(localPath.Path))
                    {
                        if (!SavePathFilter.IsExcluded(
                                Path.GetFileName(localPath.Path),
                                Path.GetFileName(localPath.Path),
                                localUnit.Exclude))
                        {
                            FileCopyHelper.DeleteFileIfExists(localPath.Path);
                        }
                    }
                    else if (localUnit.UnitType == SaveUnitType.Folder && Directory.Exists(localPath.Path))
                    {
                        // Never remove locally-owned files such as settings.json.
                        SavePathFilter.DeleteExcluding(localPath.Path, localUnit.Exclude);
                    }
                }

                if (localUnit.UnitType == SaveUnitType.WinRegistry)
                {
                    var registryFile = Directory.EnumerateFiles(sourceRoot, "*.reg").FirstOrDefault();
                    if (registryFile is not null)
                    {
                        _registryTransferService.ImportFile(registryFile);
                    }
                }
                else if (localUnit.UnitType == SaveUnitType.File)
                {
                    var targetName = Path.GetFileName(localPath.Path);
                    if (SavePathFilter.IsExcluded(targetName, targetName, localUnit.Exclude))
                    {
                        AppLogger.Info($"Restore skip excluded file: {localPath.Path}");
                        continue;
                    }

                    var sourceFile = Directory.EnumerateFiles(sourceRoot).FirstOrDefault();
                    if (sourceFile is null)
                    {
                        AppLogger.Info($"Restore skip unit {localUnit.Id}: no source file in archive");
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(localPath.Path)!);
                    FileCopyHelper.CopyFile(sourceFile, localPath.Path);
                    AppLogger.Info($"Restore unit {localUnit.Id}: copied {sourceFile} -> {localPath.Path}");
                }
                else
                {
                    // "content" is an archive-internal wrapper only: its children always go
                    // straight into the target folder. Never create target/content.
                    var contentFolder = Path.Combine(sourceRoot, "content");
                    var actualSource = Directory.Exists(contentFolder)
                        ? contentFolder // stable contents-only layout (flag-independent)
                        : (localUnit.ContentsOnly
                            ? ResolveContentsOnlyRoot(sourceRoot, localPath.Path)
                            : ResolveFolderContentRoot(sourceRoot, localPath.Path));
                    CopyDirectory(actualSource, localPath.Path, localUnit.Exclude);
                    AppLogger.Info($"Restore unit {localUnit.Id}: copied folder {actualSource} -> {localPath.Path} (contentsOnly={localUnit.ContentsOnly})");
                }
            }

            AppLogger.Info("Restore completed successfully");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Restore failed", ex);
            throw;
        }
        finally
        {
            if (Directory.Exists(extractRoot))
            {
                Directory.Delete(extractRoot, recursive: true);
            }
        }
    }

    private static string? ResolveSourceRoot(string extractRoot, int unitId, int totalUnitCount)
    {
        var unitRoot = Path.Combine(extractRoot, unitId.ToString());
        if (Directory.Exists(unitRoot))
        {
            return unitRoot;
        }

        if (totalUnitCount == 1)
        {
            var singleUnitChildDirectories = Directory.GetDirectories(extractRoot);
            if (singleUnitChildDirectories.Length == 1 && IsNumericDirectoryName(singleUnitChildDirectories[0]))
            {
                return singleUnitChildDirectories[0];
            }

            return extractRoot;
        }

        var childDirectories = Directory.GetDirectories(extractRoot);
        if (childDirectories.Length == 1)
        {
            return childDirectories[0];
        }

        var childFiles = Directory.GetFiles(extractRoot);
        if (childFiles.Length > 0)
        {
            return extractRoot;
        }

        return null;
    }

    private bool CanIncludePath(SaveUnitType unitType, string path)
    {
        if (unitType == SaveUnitType.WinRegistry)
            return _registryTransferService.KeyExists(path);
        if (unitType == SaveUnitType.Zip)
            return File.Exists(path);

        return File.Exists(path) || Directory.Exists(path);
    }

    private static bool IsNumericDirectoryName(string path)
    {
        return int.TryParse(Path.GetFileName(path), out _);
    }

    private static string ResolveFolderContentRoot(string sourceRoot, string targetPath)
    {
        var childDirectories = Directory.GetDirectories(sourceRoot);
        var childFiles = Directory.GetFiles(sourceRoot);
        if (childFiles.Length == 0 && childDirectories.Length == 1)
        {
            var targetName = Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.Equals(Path.GetFileName(childDirectories[0]), targetName, StringComparison.OrdinalIgnoreCase))
            {
                return childDirectories[0];
            }
        }

        // Name differs: keep the archived original wrapper (folder mode), so e.g.
        // restoring saves/ to target user/ results in user/saves/ rather than user/.
        return sourceRoot;
    }

    private static string ResolveContentsOnlyRoot(string sourceRoot, string targetPath)
    {
        var childDirectories = Directory.GetDirectories(sourceRoot);
        var childFiles = Directory.GetFiles(sourceRoot);
        if (childFiles.Length == 0 && childDirectories.Length == 1)
        {
            var childName = Path.GetFileName(childDirectories[0]);
            var targetName = Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            // Older mobile clients archived the physical folder ("user") even when
            // the unit was marked contents-only. Treat both common save-folder names
            // as transport wrappers so Android -> PC restores land at the save root.
            if (string.Equals(childName, targetName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(childName, "user", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(childName, "saves", StringComparison.OrdinalIgnoreCase))
            {
                return childDirectories[0];
            }
        }

        return sourceRoot;
    }

    private static void ExtractArchive(string archivePath, string extractRoot)
    {
        using var archive = ArchiveFactory.OpenArchive(archivePath);
        var options = new ExtractionOptions
        {
            ExtractFullPath = true,
            Overwrite = true,
            PreserveAttributes = false,
            PreserveFileTime = false
        };

        foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
        {
            entry.WriteToDirectory(extractRoot, options);
        }
    }

    private static void CopyDirectoryIncludingBaseDirectory(
        string sourceDirectory,
        string destinationParentDirectory,
        IReadOnlyList<string>? exclude = null)
    {
        var directoryName = Path.GetFileName(sourceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            directoryName = new DirectoryInfo(sourceDirectory).Name;
        }

        CopyDirectory(sourceDirectory, Path.Combine(destinationParentDirectory, directoryName), exclude);
    }

    /// <summary>
    /// Copies a directory's children directly into the destination without creating a
    /// base directory, so the folder's own name never enters the archive.
    /// </summary>
    private static void CopyDirectoryContents(
        string sourceDirectory,
        string destinationDirectory,
        IReadOnlyList<string>? exclude = null)
    {
        SavePathFilter.CopyDirectory(
            sourceDirectory,
            destinationDirectory,
            exclude,
            (source, destination) => FileCopyHelper.CopyFile(source, destination));
    }

    /// <summary>
    /// Copies a directory tree, optionally skipping files that match the unit's
    /// exclusion patterns (e.g. device-local <c>settings.json</c>).
    /// </summary>
    private static void CopyDirectory(
        string sourceDirectory,
        string destinationDirectory,
        IReadOnlyList<string>? exclude = null)
    {
        SavePathFilter.CopyDirectory(
            sourceDirectory,
            destinationDirectory,
            exclude,
            (source, destination) => FileCopyHelper.CopyFile(source, destination));
    }

    public string NormalizeZipArchiveForMobileRestore(string archivePath, string workingDirectory)
    {
        var extractRoot = Path.Combine(workingDirectory, "extract");
        Directory.CreateDirectory(extractRoot);
        ExtractArchive(archivePath, extractRoot);

        var rootFiles = Directory.GetFiles(extractRoot);
        var rootDirectories = Directory.GetDirectories(extractRoot);
        if (rootFiles.Length > 0 ||
            rootDirectories.Length != 1 ||
            !IsNumericDirectoryName(rootDirectories[0]))
        {
            return archivePath;
        }

        var normalizedArchivePath = Path.Combine(workingDirectory, "mobile-restore.zip");
        if (File.Exists(normalizedArchivePath))
        {
            FileCopyHelper.DeleteFileIfExists(normalizedArchivePath);
        }

        // Keep this fixed to ordinary ZIP deflate for mobile zip restore exports,
        // regardless of future archive compression presets elsewhere.
        ZipFile.CreateFromDirectory(
            rootDirectories[0],
            normalizedArchivePath,
            CompressionLevel.Optimal,
            includeBaseDirectory: false);
        return normalizedArchivePath;
    }

    private static void WriteZipComment(string archivePath, string comment)
    {
        var commentBytes = System.Text.Encoding.UTF8.GetBytes(comment);
        if (commentBytes.Length > ushort.MaxValue)
        {
            throw new InvalidOperationException("ZIP comment is too long.");
        }

        using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var endOfCentralDirectoryOffset = FindEndOfCentralDirectoryOffset(stream)
            ?? throw new InvalidDataException($"ZIP end of central directory was not found: {archivePath}");

        stream.Position = endOfCentralDirectoryOffset + 20;
        stream.WriteByte((byte)(commentBytes.Length & 0xff));
        stream.WriteByte((byte)(commentBytes.Length >> 8));
        stream.SetLength(endOfCentralDirectoryOffset + 22 + commentBytes.Length);
        stream.Position = endOfCentralDirectoryOffset + 22;
        stream.Write(commentBytes, 0, commentBytes.Length);
    }

    private static long? FindEndOfCentralDirectoryOffset(FileStream stream)
    {
        const int minimumEndOfCentralDirectorySize = 22;
        var searchLength = (int)Math.Min(stream.Length, minimumEndOfCentralDirectorySize + ushort.MaxValue);
        if (searchLength < minimumEndOfCentralDirectorySize)
        {
            return null;
        }

        var buffer = new byte[searchLength];
        stream.Position = stream.Length - searchLength;
        stream.ReadExactly(buffer);

        for (var index = searchLength - minimumEndOfCentralDirectorySize; index >= 0; index--)
        {
            if (buffer[index] == EndOfCentralDirectorySignature[0] &&
                buffer[index + 1] == EndOfCentralDirectorySignature[1] &&
                buffer[index + 2] == EndOfCentralDirectorySignature[2] &&
                buffer[index + 3] == EndOfCentralDirectorySignature[3])
            {
                return stream.Length - searchLength + index;
            }
        }

        return null;
    }
}
