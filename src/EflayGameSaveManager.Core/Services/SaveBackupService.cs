using EflayGameSaveManager.Core.Models;

namespace EflayGameSaveManager.Core.Services;

public sealed class SaveBackupService
{
    private const string RegistryExportFileName = "registry.reg";
    private readonly WinRegistryTransferService _registryTransferService = new();

    public async Task<string> BackupAsync(
        GameSnapshot game,
        string backupRoot,
        CancellationToken cancellationToken = default)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var safeGameName = CloudStoragePathHelper.SanitizeSegment(game.Name);
        var targetDirectory = Path.Combine(backupRoot, safeGameName, timestamp);

        Directory.CreateDirectory(targetDirectory);

        foreach (var saveUnit in game.SaveUnits)
        {
            foreach (var path in saveUnit.Paths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (saveUnit.UnitType == SaveUnitType.WinRegistry && _registryTransferService.KeyExists(path.Path))
                {
                    var deviceDirectory = Path.Combine(targetDirectory, path.DeviceName, saveUnit.Id.ToString());
                    _registryTransferService.ExportKey(path.Path, Path.Combine(deviceDirectory, RegistryExportFileName));
                }
                else if (File.Exists(path.Path))
                {
                    var fileName = Path.GetFileName(path.Path);
                    if (SavePathFilter.IsExcluded(fileName, fileName, saveUnit.Exclude))
                    {
                        continue;
                    }

                    var deviceDirectory = Path.Combine(targetDirectory, path.DeviceName, saveUnit.Id.ToString());
                    Directory.CreateDirectory(deviceDirectory);
                    var destination = Path.Combine(deviceDirectory, fileName);
                    if (saveUnit.UnitType == SaveUnitType.Zip)
                    {
                        var work = Path.Combine(Path.GetTempPath(), "EflayGameSaveManager", "zip-local-backup", Guid.NewGuid().ToString("N"));
                        try
                        {
                            var archive = ZipSaveLayoutService.CreateUploadArchive(path.Path, work, saveUnit.Id, saveUnit.ContentsOnly);
                            var payload = ZipSaveLayoutService.CreateRestoreZip(archive, work, saveUnit.ContentsOnly, saveUnit.Id, Path.GetFileNameWithoutExtension(path.Path));
                            await FileCopyHelper.CopyFileAsync(payload, destination, cancellationToken);
                        }
                        finally { if (Directory.Exists(work)) Directory.Delete(work, true); }
                    }
                    else await FileCopyHelper.CopyFileAsync(path.Path, destination, cancellationToken);
                }
                else if (Directory.Exists(path.Path))
                {
                    var destination = Path.Combine(targetDirectory, path.DeviceName, saveUnit.Id.ToString());
                    if (saveUnit.ContentsOnly)
                    {
                        CopyDirectory(path.Path, Path.Combine(destination, "content"), saveUnit.Exclude);
                    }
                    else
                    {
                        CopyDirectoryIncludingBaseDirectory(path.Path, destination, saveUnit.Exclude);
                    }
                }
            }
        }

        return targetDirectory;
    }

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
}
