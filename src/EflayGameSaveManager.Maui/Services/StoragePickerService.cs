using CommunityToolkit.Maui.Storage;
using Microsoft.Maui.Devices;

namespace EflayGameSaveManager.Maui.Services;

public sealed class StoragePickerService
{
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
}

public sealed record StorageSelectionResult(bool IsSuccess, string Path, string Message)
{
    public static StorageSelectionResult Success(string path, string message) => new(true, path, message);

    public static StorageSelectionResult Cancelled(string message) => new(false, string.Empty, message);
}
