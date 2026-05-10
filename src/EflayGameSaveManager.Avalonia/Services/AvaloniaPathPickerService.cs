using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using EflayGameSaveManager.Core.Models;

namespace EflayGameSaveManager.Avalonia.Services;

public sealed class AvaloniaPathPickerService : IPathPickerService
{
    private readonly Window _owner;

    public AvaloniaPathPickerService(Window owner)
    {
        _owner = owner;
    }

    public async Task<string?> PickGameExecutablePathAsync(string currentPath)
    {
        var result = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Game Executable",
            AllowMultiple = false,
            SuggestedStartLocation = await GetSuggestedStartLocationAsync(currentPath),
            FileTypeFilter =
            [
                new FilePickerFileType("Executable files")
                {
                    Patterns = OperatingSystem.IsWindows() ? ["*.exe", "*.bat", "*.cmd"] : ["*"]
                },
                FilePickerFileTypes.All
            ]
        });

        return result.Count == 0 ? null : result[0].Path.LocalPath;
    }

    public async Task<string?> PickSavePathAsync(SaveUnitType unitType, string currentPath)
    {
        if (unitType == SaveUnitType.File)
        {
            var files = await _owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Save File",
                AllowMultiple = false,
                SuggestedStartLocation = await GetSuggestedStartLocationAsync(currentPath)
            });

            return files.Count == 0 ? null : files[0].Path.LocalPath;
        }

        var folders = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Save Folder",
            AllowMultiple = false,
            SuggestedStartLocation = await GetSuggestedStartLocationAsync(currentPath)
        });

        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

    private async Task<IStorageFolder?> GetSuggestedStartLocationAsync(string currentPath)
    {
        var candidate = currentPath.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (File.Exists(candidate))
        {
            candidate = Path.GetDirectoryName(candidate) ?? candidate;
        }

        if (!Directory.Exists(candidate))
        {
            candidate = Path.GetDirectoryName(candidate) ?? string.Empty;
        }

        return Directory.Exists(candidate)
            ? await _owner.StorageProvider.TryGetFolderFromPathAsync(candidate)
            : null;
    }
}
