namespace EflayGameSaveManager.Core.Services;

internal static class FileCopyHelper
{
    public static async Task CopyFileAsync(
        string sourceFile,
        string destinationFile,
        CancellationToken cancellationToken)
    {
        EnsureDestinationCanBeReplaced(destinationFile);

        await using var source = File.Open(sourceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        await using var destination = File.Open(destinationFile, FileMode.Create, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(destination, cancellationToken);

        ClearReadOnly(destinationFile);
    }

    public static void CopyFile(string sourceFile, string destinationFile, bool overwrite = true)
    {
        EnsureDestinationCanBeReplaced(destinationFile);
        File.Copy(sourceFile, destinationFile, overwrite);
        ClearReadOnly(destinationFile);
    }

    public static void DeleteFileIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        ClearReadOnly(path);
        File.Delete(path);
    }

    private static void EnsureDestinationCanBeReplaced(string destinationFile)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationFile);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        ClearReadOnly(destinationFile);
    }

    private static void ClearReadOnly(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) == 0)
        {
            return;
        }

        File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }
}
