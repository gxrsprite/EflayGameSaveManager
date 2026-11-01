using System.IO.Compression;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services.GamePlugins;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace EflayGameSaveManager.Core.Services;

public sealed class ArchiveTransferService
{
    private const string RegistryExportFileName = "registry.reg";
    private readonly WinRegistryTransferService _registryTransferService = new();

    public string CreateCurrentDeviceArchive(
        GameSnapshot game,
        CurrentDeviceContext currentDevice,
        string workingDirectory)
    {
        Directory.CreateDirectory(workingDirectory);
        var stagingDirectory = Path.Combine(workingDirectory, "content");
        Directory.CreateDirectory(stagingDirectory);
        var configuredUnits = game.SaveUnits.Where(unit => unit.Paths.Any(path =>
            path.DeviceId == currentDevice.DeviceId && !string.IsNullOrWhiteSpace(path.Path))).ToArray();
        if (configuredUnits.Any(unit => unit.UnitType == SaveUnitType.Zip) && configuredUnits.Length != 1)
            throw new InvalidOperationException("每个设备的 ZIP 模式只能配置一个存档单元，不能与其他存档单元混合备份。");
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

        // ZIP and Folder use the same stable unit wrapper and content-mode metadata.
        if (includedUnits.Any(item => item.SaveUnit.UnitType == SaveUnitType.Zip))
        {
            if (includedUnits.Length != 1)
                throw new InvalidOperationException("每个设备的 ZIP 模式只能配置一个存档单元，不能与其他存档单元混合备份。");
            var item = includedUnits[0];
            return ZipSaveLayoutService.CreateUploadArchive(item.CurrentPath!.Path, workingDirectory,
                item.SaveUnit.Id, item.SaveUnit.ContentsOnly);
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

        ZipSaveLayoutService.WriteArchiveMetadata(archivePath, includedUnits
            .Where(item => item.SaveUnit.UnitType == SaveUnitType.Folder)
            .Select(item => new ZipSaveLayoutService.UnitLayout(item.SaveUnit.Id,
                Path.GetFileName(item.CurrentPath!.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), item.SaveUnit.ContentsOnly)));
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
        // Export ZIP payloads using the destination unit's content mode.
        var zipUnits = game.SaveUnits
            .Where(u => u.UnitType == SaveUnitType.Zip)
            .Select(u => new { Unit = u, Path = u.Paths.FirstOrDefault(p => string.Equals(p.DeviceId, currentDevice.DeviceId, StringComparison.Ordinal)) })
            .Where(x => x.Path is not null && !string.IsNullOrWhiteSpace(x.Path.Path))
            .ToArray();

        if (zipUnits.Length > 0)
        {
            if (game.SaveUnits.Count(unit => unit.Paths.Any(path => path.DeviceId == currentDevice.DeviceId && !string.IsNullOrWhiteSpace(path.Path))) != 1)
                throw new InvalidOperationException("每个设备的 ZIP 模式只能配置一个存档单元，不能与其他存档单元混合恢复。");
            var zipWorkRoot = Path.Combine(Path.GetTempPath(), "EflayGameSaveManager", "zip-restore", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(zipWorkRoot);
            try
            {
                foreach (var item in zipUnits)
                {
                    var normalizedArchivePath = NormalizeZipArchiveForMobileRestore(archivePath, zipWorkRoot,
                        item.Unit.ContentsOnly, item.Unit.Id, Path.GetFileNameWithoutExtension(item.Path!.Path));
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
                    var actualSource = Directory.Exists(contentFolder) &&
                        ZipSaveLayoutService.ReadUnitContentsOnly(archivePath, Path.GetFileName(sourceRoot)) != false
                        ? contentFolder // stable contents-only layout (flag-independent)
                        : ResolveFolderContentRoot(sourceRoot, localPath.Path, localUnit.ContentsOnly);
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

    private static string ResolveFolderContentRoot(string sourceRoot, string targetPath, bool contentsOnly)
    {
        var childDirectories = Directory.GetDirectories(sourceRoot);
        var childFiles = Directory.GetFiles(sourceRoot);
        if (childFiles.Length == 0 && childDirectories.Length == 1)
        {
            var targetName = Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            // Older Android archives keep the source folder (e.g. 0/user/*).
            // A contents-only target must unwrap it even when it is named differently
            // from the PC destination (saves), otherwise the game reads stale root files.
            if (contentsOnly || string.Equals(Path.GetFileName(childDirectories[0]), targetName, StringComparison.OrdinalIgnoreCase))
            {
                return childDirectories[0];
            }
        }

        // Name differs in folder mode: keep the archived original wrapper, so e.g.
        // restoring saves/ to target user/ results in user/saves/ rather than user/.
        return sourceRoot;
    }

    internal static void ExtractArchive(string archivePath, string extractRoot)
    {
        using var archive = ArchiveFactory.OpenArchive(archivePath);
        var options = new ExtractionOptions
        {
            ExtractFullPath = true,
            Overwrite = true,
            PreserveAttributes = false,
            PreserveFileTime = true
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

    public string NormalizeZipArchiveForMobileRestore(string archivePath, string workingDirectory,
        bool contentsOnly = false, int? unitId = null, string? fallbackFolderName = null) =>
        ZipSaveLayoutService.CreateRestoreZip(archivePath, workingDirectory, contentsOnly, unitId, fallbackFolderName);

}
