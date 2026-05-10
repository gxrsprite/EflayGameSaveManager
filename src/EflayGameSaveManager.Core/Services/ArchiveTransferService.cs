using System.IO.Compression;
using EflayGameSaveManager.Core.Models;
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

        foreach (var saveUnit in game.SaveUnits)
        {
            var currentPath = saveUnit.Paths.FirstOrDefault(path => string.Equals(path.DeviceId, currentDevice.DeviceId, StringComparison.Ordinal));
            if (currentPath is null || string.IsNullOrWhiteSpace(currentPath.Path))
            {
                continue;
            }

            if (saveUnit.UnitType == SaveUnitType.WinRegistry)
            {
                if (!_registryTransferService.KeyExists(currentPath.Path))
                {
                    continue;
                }
            }
            else if (!File.Exists(currentPath.Path) && !Directory.Exists(currentPath.Path))
            {
                continue;
            }

            var unitRelativePath = saveUnit.Id.ToString();
            var stagedPath = Path.Combine(stagingDirectory, unitRelativePath);

            if (saveUnit.UnitType == SaveUnitType.WinRegistry)
            {
                _registryTransferService.ExportKey(currentPath.Path, Path.Combine(stagedPath, RegistryExportFileName));
            }
            else if (saveUnit.UnitType == SaveUnitType.File)
            {
                Directory.CreateDirectory(stagedPath);
                File.Copy(currentPath.Path, Path.Combine(stagedPath, Path.GetFileName(currentPath.Path)), overwrite: true);
            }
            else
            {
                CopyDirectory(currentPath.Path, stagedPath);
            }
        }

        var archivePath = Path.Combine(workingDirectory, "current-device-save.zip");
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        ZipFile.CreateFromDirectory(stagingDirectory, archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return archivePath;
    }

    public void RestoreCurrentDeviceArchive(
        string archivePath,
        GameSnapshot game,
        CurrentDeviceContext currentDevice)
    {
        var extractRoot = Path.Combine(Path.GetTempPath(), "EflayGameSaveManager", "restore", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractRoot);
        try
        {
            ExtractArchive(archivePath, extractRoot);

            foreach (var localUnit in game.SaveUnits)
            {
                var localPath = localUnit.Paths.FirstOrDefault(path => string.Equals(path.DeviceId, currentDevice.DeviceId, StringComparison.Ordinal));
                if (localPath is null || string.IsNullOrWhiteSpace(localPath.Path))
                {
                    continue;
                }

                var sourceRoot = ResolveSourceRoot(extractRoot, localUnit.Id, game.SaveUnits.Count);
                if (sourceRoot is null)
                {
                    continue;
                }

                if (localUnit.DeleteBeforeApply)
                {
                    if (localUnit.UnitType == SaveUnitType.WinRegistry)
                    {
                        _registryTransferService.DeleteKey(localPath.Path);
                    }
                    else if (localUnit.UnitType == SaveUnitType.File && File.Exists(localPath.Path))
                    {
                        File.Delete(localPath.Path);
                    }
                    else if (localUnit.UnitType == SaveUnitType.Folder && Directory.Exists(localPath.Path))
                    {
                        Directory.Delete(localPath.Path, recursive: true);
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
                    var sourceFile = Directory.EnumerateFiles(sourceRoot).FirstOrDefault();
                    if (sourceFile is null)
                    {
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(localPath.Path)!);
                    File.Copy(sourceFile, localPath.Path, overwrite: true);
                }
                else
                {
                    CopyDirectory(ResolveFolderContentRoot(sourceRoot, localPath.Path), localPath.Path);
                }
            }
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

    private static string ResolveFolderContentRoot(string sourceRoot, string targetPath)
    {
        var childDirectories = Directory.GetDirectories(sourceRoot);
        var childFiles = Directory.GetFiles(sourceRoot);
        if (childFiles.Length == 0 && childDirectories.Length == 1)
        {
            var targetDirectoryName = Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var childDirectoryName = Path.GetFileName(childDirectories[0]);
            if (string.Equals(childDirectoryName, targetDirectoryName, StringComparison.OrdinalIgnoreCase))
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
            PreserveFileTime = true
        };

        foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
        {
            entry.WriteToDirectory(extractRoot, options);
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.GetFiles(sourceDirectory))
        {
            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDirectory))
        {
            CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)));
        }
    }
}
