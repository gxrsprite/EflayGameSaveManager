using CommunityToolkit.Maui.Storage;
using Microsoft.Maui.Devices;

namespace EflayGameSaveManager.Maui.Services;

public sealed class StoragePickerService
{
    public async Task<StorageSelectionResult> PickCustomPathAsync(string initialPath, bool folder, bool zip = false)
    {
        var page = Application.Current?.Windows.FirstOrDefault(window => window.Page is not null)?.Page
            ?? throw new InvalidOperationException("当前窗口尚未准备就绪。");
        var picker = new CustomPathPage(initialPath, folder, zip);
        await page.Navigation.PushModalAsync(picker);
        return await picker.Result;
    }

    public async Task<StorageSelectionResult> PickConfiguredZipAsync()
    {
#if ANDROID
        try
        {
            var uri = await Platforms.Android.ZipDocumentAccess.PickAsync();
            return uri is null ? StorageSelectionResult.Cancelled("已取消 ZIP 文件选择。")
                : StorageSelectionResult.Success(uri, "已选择 ZIP 文件，将使用原文件进行备份和恢复。");
        }
        catch (Exception ex) { return StorageSelectionResult.Cancelled(ex.Message); }
#else
        return await PickZipFileAsync();
#endif
    }

    public async Task CopyConfiguredZipToLocalAsync(string path, string localPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
#if ANDROID
        if (path.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            using var uri = global::Android.Net.Uri.Parse(path)!;
            await using var input = global::Android.App.Application.Context.ContentResolver!.OpenInputStream(uri)
                ?? throw new IOException("无法读取所选 ZIP 文件，请重新选择并授权。");
            await using var output = File.Create(localPath);
            await input.CopyToAsync(output);
            return;
        }
        if (Platforms.Android.ShizukuFileService.IsRestrictedPath(path))
        {
            var stagingRoot = Path.Combine(Path.GetDirectoryName(localPath)!, "restricted-source");
            try
            {
                var staged = await Platforms.Android.ShizukuFileService.CopyFromRestrictedAsync(path, stagingRoot)
                    ?? throw new IOException("无法读取 ZIP 文件。" + Platforms.Android.ShizukuFileService.StatusMessage);
                File.Copy(staged, localPath, true);
            }
            finally { if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, true); }
            return;
        }
#endif
        File.Copy(path, localPath, true);
    }

    public async Task WriteConfiguredZipAsync(string source, string path)
    {
        // Preserve the previous archive before replacing a configured document.
        var backup = Path.Combine(FileSystem.AppDataDirectory, "zip-before-restore", Guid.NewGuid().ToString("N"), "previous.zip");
        await CopyConfiguredZipToLocalAsync(path, backup);
#if ANDROID
        if (path.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            using var uri = global::Android.Net.Uri.Parse(path)!;
            await using var input = File.OpenRead(source);
            await using var output = global::Android.App.Application.Context.ContentResolver!.OpenOutputStream(uri, "rwt")
                ?? throw new IOException("所选 ZIP 文件无法写入，请重新选择可读写的文件。");
            await input.CopyToAsync(output);
            await output.FlushAsync();
            return;
        }
        if (Platforms.Android.ShizukuFileService.IsRestrictedPath(path))
        {
            if (!await Platforms.Android.ShizukuFileService.CopyToRestrictedAsync(source, path))
                throw new IOException("ZIP 恢复失败。" + Platforms.Android.ShizukuFileService.StatusMessage);
            return;
        }
#endif
        File.Copy(source, path, true);
    }

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
