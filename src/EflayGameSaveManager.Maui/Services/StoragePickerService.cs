using CommunityToolkit.Maui.Storage;
using Microsoft.Maui.Devices;

namespace EflayGameSaveManager.Maui.Services;

public sealed class StoragePickerService
{
    private static readonly FilePickerFileType ZipFileTypes = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.Android] = ["application/zip", "application/x-zip-compressed", "*/*"],
        [DevicePlatform.WinUI] = [".zip"],
        [DevicePlatform.iOS] = ["public.zip-archive"],
        [DevicePlatform.MacCatalyst] = ["public.zip-archive"]
    });

    public async Task<StorageSelectionResult> PickFileAsync(CancellationToken cancellationToken = default)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select a save file"
        });

        if (file is null)
        {
            return StorageSelectionResult.Cancelled("File selection cancelled.");
        }

        var path = string.IsNullOrWhiteSpace(file.FullPath)
            ? file.FileName
            : file.FullPath;
        var message = string.IsNullOrWhiteSpace(file.FullPath)
            ? "File selected, but Android returned a provider-backed entry. Path bridging is still pending."
            : $"File selected: {path}";

        return StorageSelectionResult.Success(path, message);
    }

    public async Task<StorageSelectionResult> PickZipFileAsync(CancellationToken cancellationToken = default)
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Select a zip file to sync",
            FileTypes = ZipFileTypes
        });

        if (file is null)
        {
            return StorageSelectionResult.Cancelled("Zip selection cancelled.");
        }

        var path = await MaterializePickedFileAsync(file, cancellationToken);
        var message = $"Zip selected: {path}";

        return StorageSelectionResult.Success(path, message);
    }

    public async Task<StorageSelectionResult> PickFolderAsync(CancellationToken cancellationToken = default)
    {
        var result = await FolderPicker.Default.PickAsync(cancellationToken);
        if (!result.IsSuccessful || result.Folder is null)
        {
            return StorageSelectionResult.Cancelled(result.Exception?.Message ?? "Folder selection cancelled.");
        }

        var path = result.Folder.Path;
        var message = DeviceInfo.Current.Platform == DevicePlatform.Android
            ? "Folder selected. Android may expose it through SAF, so direct System.IO sync still needs the next bridge layer."
            : $"Folder selected: {path}";

        return StorageSelectionResult.Success(path, message);
    }

    public async Task<StorageSelectionResult> SaveFileAsync(
        string suggestedFileName,
        Stream sourceStream,
        CancellationToken cancellationToken = default)
    {
        var result = await FileSaver.Default.SaveAsync(
            suggestedFileName,
            sourceStream,
            cancellationToken);

        if (!result.IsSuccessful)
        {
            return StorageSelectionResult.Cancelled(result.Exception?.Message ?? "File save cancelled.");
        }

        return StorageSelectionResult.Success(
            result.FilePath,
            $"File saved: {result.FilePath}");
    }

    private static async Task<string> MaterializePickedFileAsync(FileResult file, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(file.FullPath) && File.Exists(file.FullPath))
        {
            return file.FullPath;
        }

        var extension = Path.GetExtension(file.FileName);
        var tempFileName = $"{Guid.NewGuid():N}{extension}";
        var tempPath = Path.Combine(FileSystem.CacheDirectory, "picked-files", tempFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

        await using var sourceStream = await file.OpenReadAsync();
        await using var destinationStream = File.Create(tempPath);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        await destinationStream.FlushAsync(cancellationToken);
        return tempPath;
    }
}

public sealed record StorageSelectionResult(bool IsSuccess, string Path, string Message)
{
    public static StorageSelectionResult Success(string path, string message) => new(true, path, message);

    public static StorageSelectionResult Cancelled(string message) => new(false, string.Empty, message);
}
