using EflayGameSaveManager.Core.Models;

namespace EflayGameSaveManager.Core.Services;

public sealed class SaveBackupService
{
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

                if (File.Exists(path.Path))
                {
                    var deviceDirectory = Path.Combine(targetDirectory, path.DeviceName, $"unit-{saveUnit.Id}");
                    Directory.CreateDirectory(deviceDirectory);
                    var destination = Path.Combine(deviceDirectory, Path.GetFileName(path.Path));
                    await CopyFileAsync(path.Path, destination, cancellationToken);
                }
                else if (Directory.Exists(path.Path))
                {
                    var destination = Path.Combine(targetDirectory, path.DeviceName, $"unit-{saveUnit.Id}");
                    CopyDirectory(path.Path, destination);
                }
            }
        }

        return targetDirectory;
    }

    private static async Task CopyFileAsync(string sourceFile, string destinationFile, CancellationToken cancellationToken)
    {
        await using var source = File.OpenRead(sourceFile);
        await using var destination = File.Create(destinationFile);
        await source.CopyToAsync(destination, cancellationToken);
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
