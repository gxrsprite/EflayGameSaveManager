using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services;

namespace EflayGameSaveManager.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly GameSaveManagerConfigurationService _configurationService;
    private readonly GameLibraryService _gameLibraryService;
    private readonly CloudSyncService _cloudSyncService;

    private ManagerConfig? _currentConfig;
    private AppSnapshot? _currentSnapshot;
    private string? _pendingCloudBackupOverwritePath;

    [ObservableProperty]
    private string _configPath = "Loading...";

    [ObservableProperty]
    private string _backupPath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Loading configuration...";

    [ObservableProperty]
    private string _version = string.Empty;

    [ObservableProperty]
    private string _cloudSummary = "Cloud sync not executed.";

    [ObservableProperty]
    private bool _cloudEnabled;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _currentDeviceName = string.Empty;

    [ObservableProperty]
    private string _logPath = AppLogger.LogFilePath;

    [ObservableProperty]
    private bool _isHeaderExpanded;

    [ObservableProperty]
    private GameListItemViewModel? _selectedGame;

    [ObservableProperty]
    private bool _hasSelectedGame;

    [ObservableProperty]
    private string _selectedGameName = "Select a game";

    [ObservableProperty]
    private string _selectedGameStats = string.Empty;

    [ObservableProperty]
    private string _selectedGameSaveSummary = string.Empty;

    [ObservableProperty]
    private string _selectedGameExecutableSummary = string.Empty;

    [ObservableProperty]
    private string _currentDeviceGamePath = string.Empty;

    [ObservableProperty]
    private string _selectedGameCloudState = "Cloud status not loaded.";

    [ObservableProperty]
    private bool _selectedGameCanSync;

    [ObservableProperty]
    private bool _selectedGameCloudAvailable;

    [ObservableProperty]
    private CloudBackupItemViewModel? _selectedCloudBackup;

    [ObservableProperty]
    private bool _hasSelectedCloudBackup;

    public ObservableCollection<GameListItemViewModel> Games { get; } = [];
    public ObservableCollection<CurrentSavePathEditorViewModel> CurrentDevicePaths { get; } = [];
    public ObservableCollection<CloudBackupItemViewModel> CloudBackups { get; } = [];

    public MainWindowViewModel(
        GameSaveManagerConfigurationService configurationService,
        GameLibraryService gameLibraryService,
        CloudSyncService cloudSyncService)
    {
        _configurationService = configurationService;
        _gameLibraryService = gameLibraryService;
        _cloudSyncService = cloudSyncService;
    }

    public Task InitializeAsync() => ReloadAsync();

    partial void OnSelectedGameChanged(GameListItemViewModel? value)
    {
        _ = HandleSelectedGameChangedAsync(value);
    }

    partial void OnSelectedCloudBackupChanged(CloudBackupItemViewModel? value)
    {
        HasSelectedCloudBackup = value is not null;
        _pendingCloudBackupOverwritePath = null;
    }

    [RelayCommand]
    private void ToggleHeader()
    {
        IsHeaderExpanded = !IsHeaderExpanded;
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        await RunBusyAsync(
            "Loading configuration...",
            async () =>
            {
                var configPath = _configurationService.FindConfigPath();
                var config = await _configurationService.LoadAsync(configPath);
                var snapshot = _gameLibraryService.CreateSnapshot(config, configPath);

                ApplySnapshot(config, snapshot);
                StatusMessage = $"Loaded {Games.Count} games for {snapshot.CurrentDevice.DeviceName}.";
            });
    }

    [RelayCommand]
    private async Task RefreshCloudStatusAsync()
    {
        if (!TryGetCloudContext(out _, out _, out _))
        {
            return;
        }

        await RunBusyAsync(
            "Refreshing selected game cloud status...",
            async () =>
            {
                await RefreshSelectedGameCloudStatusAsync(forceReload: true);
            });
    }

    [RelayCommand]
    private async Task UploadAllBackupsToCloudAsync()
    {
        if (!TryGetCloudContext(out var config, out var snapshot, out var cloudSettings))
        {
            return;
        }

        await RunBusyAsync(
            "Uploading all backups to cloud...",
            async () =>
            {
                var result = await _cloudSyncService.UploadAllBackupsAsync(snapshot, cloudSettings);
                CloudSummary =
                    $"Uploaded {result.UploadedObjectCount} objects to {config.Settings.CloudSettings.Backend.Bucket}/{result.RootKey}";
                StatusMessage = "All game backups uploaded.";
                await RefreshSelectedGameCloudStatusAsync(forceReload: true);
            });
    }

    [RelayCommand]
    private async Task SaveCurrentDevicePathsAsync()
    {
        if (_currentConfig is null || _currentSnapshot is null || SelectedGame is null)
        {
            return;
        }

        await RunBusyAsync(
            $"Saving current-device paths for {SelectedGame.Name}...",
            async () =>
            {
                var game = _currentConfig.Games.First(item => string.Equals(item.Name, SelectedGame.Name, StringComparison.Ordinal));
                foreach (var pathEditor in CurrentDevicePaths)
                {
                    var saveUnit = game.SavePaths.First(unit => unit.Id == pathEditor.SaveUnitId);
                    saveUnit.Paths[_currentSnapshot.CurrentDevice.DeviceId] = pathEditor.Path.Trim();
                }

                if (!string.IsNullOrWhiteSpace(CurrentDeviceGamePath))
                {
                    game.GamePaths[_currentSnapshot.CurrentDevice.DeviceId] = CurrentDeviceGamePath.Trim();
                }

                await _configurationService.SaveAsync(_currentSnapshot.ConfigPath, _currentConfig);
                var refreshedSnapshot = _gameLibraryService.CreateSnapshot(_currentConfig, _currentSnapshot.ConfigPath);
                ApplySnapshot(_currentConfig, refreshedSnapshot, SelectedGame.Name);
                StatusMessage = $"Saved current-device paths for {SelectedGame.Name}.";
            });
    }

    [RelayCommand]
    private void OpenCurrentGameFolder()
    {
        OpenPath(CurrentDeviceGamePath);
    }

    [RelayCommand]
    private void RunCurrentGame()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(CurrentDeviceGamePath))
            {
                StatusMessage = "Current device game path is empty.";
                return;
            }

            if (!File.Exists(CurrentDeviceGamePath))
            {
                StatusMessage = $"Game executable not found: {CurrentDeviceGamePath}";
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = CurrentDeviceGamePath,
                WorkingDirectory = Path.GetDirectoryName(CurrentDeviceGamePath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to run game: {CurrentDeviceGamePath}", ex);
            StatusMessage = $"{ex.Message} | Log: {LogPath}";
        }
    }

    [RelayCommand]
    private async Task UploadSelectedGameCurrentSaveAsync()
    {
        if (!TryGetSelectedCloudContext(out var game, out var config, out var snapshot, out var cloudSettings))
        {
            return;
        }

        await RunBusyAsync(
            $"Uploading current save for {game.Name}...",
            async () =>
            {
                var result = await _cloudSyncService.UploadGameCurrentSaveAsync(game, snapshot.CurrentDevice, cloudSettings);
                CloudSummary =
                    $"Uploaded compressed save to {config.Settings.CloudSettings.Backend.Bucket}/{result.RootKey}";
                StatusMessage = $"Uploaded current compressed save for {game.Name}.";
                await RefreshSelectedGameCloudStatusAsync(forceReload: true);
            });
    }

    [RelayCommand]
    private async Task RestoreSelectedGameCurrentSaveAsync()
    {
        if (!TryGetSelectedCloudContext(out var game, out var config, out var snapshot, out var cloudSettings))
        {
            return;
        }
        AppLogger.Info(
            $"UI restore-current requested: game={game.Name}, device={snapshot.CurrentDevice.DeviceName}[{snapshot.CurrentDevice.DeviceId}], endpoint={cloudSettings.Backend.Endpoint}, bucket={cloudSettings.Backend.Bucket}, root={cloudSettings.RootPath}");

        await RunBusyAsync(
            $"Restoring cloud save for {game.Name}...",
            async () =>
            {
                var result = await _cloudSyncService.RestoreGameCurrentSaveAsync(game, snapshot.CurrentDevice, cloudSettings);
                CloudSummary =
                    $"Downloaded compressed save from {config.Settings.CloudSettings.Backend.Bucket}/{result.RootKey}";
                StatusMessage = $"Restored current cloud save for {game.Name}.";
                await RefreshSelectedGameCloudStatusAsync(forceReload: true);
            });
    }

    [RelayCommand]
    private async Task DownloadSelectedCloudBackupZipAsync()
    {
        if (!TryGetSelectedCloudContext(out var game, out var config, out _, out var cloudSettings))
        {
            return;
        }

        if (SelectedCloudBackup is null)
        {
            StatusMessage = "No cloud backup is selected.";
            return;
        }

        var backupDate = SelectedCloudBackup.Date;
        var destinationPath = GetCloudBackupZipDownloadPath(game.Name, backupDate);
        if (File.Exists(destinationPath) &&
            !string.Equals(_pendingCloudBackupOverwritePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            _pendingCloudBackupOverwritePath = destinationPath;
            StatusMessage = $"Zip already exists: {destinationPath}. Click Download Zip again to overwrite.";
            return;
        }

        await RunBusyAsync(
            $"Downloading cloud backup zip {backupDate} for {game.Name}...",
            async () =>
            {
                var result = await _cloudSyncService.DownloadGameBackupArchiveAsync(
                    game,
                    cloudSettings,
                    backupDate,
                    destinationPath,
                    overwrite: true);
                CloudSummary =
                    $"Downloaded compressed save from {config.Settings.CloudSettings.Backend.Bucket}/{result.RootKey}";
                StatusMessage = $"Downloaded cloud backup zip to {destinationPath}.";
                _pendingCloudBackupOverwritePath = null;
            });
    }

    [RelayCommand]
    private async Task RestoreSelectedCloudBackupAsync()
    {
        if (!TryGetSelectedCloudContext(out var game, out var config, out var snapshot, out var cloudSettings))
        {
            return;
        }

        if (SelectedCloudBackup is null)
        {
            StatusMessage = "No cloud backup is selected.";
            return;
        }

        var backupDate = SelectedCloudBackup.Date;
        AppLogger.Info(
            $"UI restore-selected-backup requested: game={game.Name}, backupDate={backupDate}, device={snapshot.CurrentDevice.DeviceName}[{snapshot.CurrentDevice.DeviceId}], endpoint={cloudSettings.Backend.Endpoint}, bucket={cloudSettings.Backend.Bucket}, root={cloudSettings.RootPath}");
        await RunBusyAsync(
            $"Restoring cloud backup {backupDate} for {game.Name}...",
            async () =>
            {
                var result = await _cloudSyncService.RestoreGameBackupAsync(
                    game,
                    snapshot.CurrentDevice,
                    cloudSettings,
                    backupDate);
                CloudSummary =
                    $"Restored compressed save from {config.Settings.CloudSettings.Backend.Bucket}/{result.RootKey}";
                StatusMessage = $"Restored cloud backup {backupDate} for {game.Name}.";
                await RefreshSelectedGameCloudStatusAsync(forceReload: true);
            });
    }

    [RelayCommand]
    private async Task DeleteSelectedCloudBackupAsync()
    {
        if (!TryGetSelectedCloudContext(out var game, out _, out var snapshot, out var cloudSettings))
        {
            return;
        }

        if (SelectedCloudBackup is null)
        {
            StatusMessage = "No cloud backup is selected.";
            return;
        }

        var backupDate = SelectedCloudBackup.Date;
        await RunBusyAsync(
            $"Deleting cloud backup {backupDate} for {game.Name}...",
            async () =>
            {
                await _cloudSyncService.DeleteGameBackupAsync(
                    game,
                    snapshot.CurrentDevice,
                    cloudSettings,
                    backupDate);
                StatusMessage = $"Deleted cloud backup {backupDate} for {game.Name}.";
                await RefreshSelectedGameCloudStatusAsync(forceReload: true);
            });
    }

    private async Task HandleSelectedGameChangedAsync(GameListItemViewModel? selectedGame)
    {
        CurrentDevicePaths.Clear();
        CloudBackups.Clear();
        SelectedCloudBackup = null;

        if (_currentSnapshot is null || selectedGame is null)
        {
            HasSelectedGame = false;
            SelectedGameName = "Select a game";
            SelectedGameStats = string.Empty;
            SelectedGameSaveSummary = string.Empty;
            SelectedGameExecutableSummary = string.Empty;
            CurrentDeviceGamePath = string.Empty;
            SelectedGameCloudState = "Cloud status not loaded.";
            SelectedGameCanSync = false;
            SelectedGameCloudAvailable = false;
            return;
        }

        var game = _currentSnapshot.Games.First(item => string.Equals(item.Name, selectedGame.Name, StringComparison.Ordinal));
        ApplySelectedGameSnapshot(game);
        await RefreshSelectedGameCloudStatusAsync(forceReload: false);
    }

    private void ApplySnapshot(ManagerConfig config, AppSnapshot snapshot, string? selectedGameName = null)
    {
        _currentConfig = config;
        _currentSnapshot = snapshot;

        ConfigPath = snapshot.ConfigPath;
        BackupPath = snapshot.BackupRoot;
        Version = config.Version;
        CurrentDeviceName = snapshot.CurrentDevice.DeviceName;
        CloudEnabled = IsCloudConfigured(config.Settings.CloudSettings);
        IsHeaderExpanded = false;
        CloudSummary = CloudEnabled
            ? $"Cloud target: {config.Settings.CloudSettings.Backend.Bucket}/{CloudStoragePathHelper.NormalizeRootPath(config.Settings.CloudSettings.RootPath)}"
            : "Cloud backend is not configured.";

        Games.Clear();
        foreach (var game in snapshot.Games)
        {
            Games.Add(new GameListItemViewModel(game.Name, game.CloudSyncEnabled));
        }

        SelectedGame = Games.FirstOrDefault(item =>
                           string.Equals(item.Name, selectedGameName, StringComparison.Ordinal))
                       ?? Games.FirstOrDefault();
    }

    private void ApplySelectedGameSnapshot(GameSnapshot game)
    {
        HasSelectedGame = true;
        SelectedGameName = game.Name;
        SelectedGameStats = $"Save units: {game.SaveUnits.Count} | Devices: {game.SaveUnits.SelectMany(unit => unit.Paths).Select(path => path.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase).Count()}";
        SelectedGameSaveSummary = string.Join(
            Environment.NewLine,
            game.SaveUnits.SelectMany(unit => unit.Paths.Select(path => $"[{path.DeviceName}] {path.Path}")));
        SelectedGameExecutableSummary = game.GamePaths.Count == 0
            ? "No executable path configured."
            : string.Join(Environment.NewLine, game.GamePaths.Select(path => $"[{path.DeviceName}] {path.Path}"));
        CurrentDeviceGamePath = game.CurrentDeviceGamePath.Path;
        SelectedGameCanSync = CloudEnabled && game.CloudSyncEnabled;
        SelectedGameCloudAvailable = false;
        SelectedGameCloudState = game.CloudSyncEnabled ? "Checking cloud status..." : "Cloud sync disabled.";
        CloudBackups.Clear();
        SelectedCloudBackup = null;

        CurrentDevicePaths.Clear();
        foreach (var currentPath in game.CurrentDevicePaths)
        {
            CurrentDevicePaths.Add(new CurrentSavePathEditorViewModel(
                currentPath.SaveUnitId,
                currentPath.UnitType,
                currentPath.Path,
                OpenPath));
        }
    }

    private void OpenPath(string path)
    {
        try
        {
            var target = path;
            if (string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            if (File.Exists(target))
            {
                target = Path.GetDirectoryName(target) ?? target;
            }

            if (!Directory.Exists(target))
            {
                StatusMessage = $"Directory not found: {target}";
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to open path: {path}", ex);
            StatusMessage = $"{ex.Message} | Log: {LogPath}";
        }
    }

    private async Task RefreshSelectedGameCloudStatusAsync(bool forceReload)
    {
        if (!TryGetSelectedCloudContext(out var game, out _, out var snapshot, out var cloudSettings))
        {
            return;
        }
        AppLogger.Info(
            $"UI refresh-cloud-status: game={game.Name}, forceReload={forceReload}, device={snapshot.CurrentDevice.DeviceName}[{snapshot.CurrentDevice.DeviceId}], endpoint={cloudSettings.Backend.Endpoint}, bucket={cloudSettings.Backend.Bucket}, root={cloudSettings.RootPath}");

        if (!forceReload && !string.Equals(SelectedGameCloudState, "Checking cloud status...", StringComparison.Ordinal))
        {
            SelectedGameCloudState = "Checking cloud status...";
        }

        var status = await _cloudSyncService.GetGameCurrentStatusAsync(game, snapshot.CurrentDevice, cloudSettings);
        var backups = await _cloudSyncService.ListGameBackupsAsync(game, snapshot.CurrentDevice, cloudSettings);
        SelectedGameCloudAvailable = status.IsAvailable;
        SelectedGameCloudState = status.IsAvailable
            ? $"Cloud backups: {status.BackupCount}, current head: {status.CurrentHead}"
            : $"Cloud backups: {status.BackupCount}, current head: not found";

        CloudBackups.Clear();
        foreach (var backup in backups)
        {
            CloudBackups.Add(new CloudBackupItemViewModel(backup));
        }

        SelectedCloudBackup = CloudBackups.FirstOrDefault(item => item.IsCurrentDeviceHead)
                              ?? CloudBackups.FirstOrDefault();

        var listItem = Games.FirstOrDefault(item => string.Equals(item.Name, status.GameName, StringComparison.Ordinal));
        listItem?.SetCloudStatus(status);
    }

    private static string GetCloudBackupZipDownloadPath(string gameName, string backupDate)
    {
        var safeGameName = SanitizeFileName(gameName);
        var safeBackupDate = SanitizeFileName(backupDate);
        return Path.Combine(AppContext.BaseDirectory, "save_data", safeGameName, $"{safeBackupDate}.zip");
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = value
            .Select(character => invalidChars.Contains(character) ? '_' : character)
            .ToArray();
        var result = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(result) ? "_" : result;
    }

    private bool TryGetSelectedCloudContext(
        out GameSnapshot game,
        out ManagerConfig config,
        out AppSnapshot snapshot,
        out CloudSettings cloudSettings)
    {
        game = default!;
        config = _currentConfig!;
        snapshot = _currentSnapshot!;
        cloudSettings = config?.Settings.CloudSettings ?? new CloudSettings();

        if (!TryGetCloudContext(out config, out snapshot, out cloudSettings))
        {
            return false;
        }

        if (SelectedGame is null)
        {
            StatusMessage = "No game is selected.";
            return false;
        }

        game = snapshot.Games.FirstOrDefault(item => string.Equals(item.Name, SelectedGame.Name, StringComparison.Ordinal))!;
        if (game is null)
        {
            StatusMessage = $"Game not found: {SelectedGame.Name}";
            return false;
        }

        return true;
    }

    private bool TryGetCloudContext(
        out ManagerConfig config,
        out AppSnapshot snapshot,
        out CloudSettings cloudSettings)
    {
        config = _currentConfig!;
        snapshot = _currentSnapshot!;
        cloudSettings = config?.Settings.CloudSettings ?? new CloudSettings();

        if (_currentConfig is null || _currentSnapshot is null)
        {
            StatusMessage = "Configuration is not loaded yet.";
            return false;
        }

        if (!CloudEnabled)
        {
            StatusMessage = "Cloud backend is not configured.";
            return false;
        }

        config = _currentConfig;
        snapshot = _currentSnapshot;
        cloudSettings = _currentConfig.Settings.CloudSettings;
        return true;
    }

    private async Task RunBusyAsync(string busyMessage, Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = busyMessage;
            await action();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"UI operation failed: {busyMessage}", ex);
            StatusMessage = $"{ex.Message} | Log: {LogPath}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool IsCloudConfigured(CloudSettings cloudSettings)
    {
        return string.Equals(cloudSettings.Backend.Type, "S3", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(cloudSettings.Backend.Endpoint) &&
               !string.IsNullOrWhiteSpace(cloudSettings.Backend.Bucket) &&
               !string.IsNullOrWhiteSpace(cloudSettings.Backend.AccessKeyId) &&
               !string.IsNullOrWhiteSpace(cloudSettings.Backend.SecretAccessKey);
    }
}

public sealed partial class GameListItemViewModel : ObservableObject
{
    public GameListItemViewModel(string name, bool cloudSyncEnabled)
    {
        Name = name;
        CloudSyncEnabled = cloudSyncEnabled;
    }

    [ObservableProperty]
    private bool _cloudSaveAvailable;

    [ObservableProperty]
    private string _cloudBadge = "Cloud: ?";

    public string Name { get; }

    public bool CloudSyncEnabled { get; }

    public void SetCloudStatus(GameCloudStatus status)
    {
        CloudSaveAvailable = status.IsAvailable;
        CloudBadge = status.IsAvailable ? "Cloud: Ready" : "Cloud: Empty";
    }
}

public sealed partial class CurrentSavePathEditorViewModel : ObservableObject
{
    public CurrentSavePathEditorViewModel(int saveUnitId, SaveUnitType unitType, string path, Action<string> openPath)
    {
        SaveUnitId = saveUnitId;
        UnitType = unitType;
        this.path = path;
        OpenPathCommand = new RelayCommand(() => openPath(Path));
    }

    public int SaveUnitId { get; }

    public SaveUnitType UnitType { get; }

    public IRelayCommand OpenPathCommand { get; }

    [ObservableProperty]
    private string path;
}

public sealed class CloudBackupItemViewModel
{
    public CloudBackupItemViewModel(CloudGameBackup backup)
    {
        Date = backup.Date;
        Describe = string.IsNullOrWhiteSpace(backup.Describe) ? "-" : backup.Describe;
        Path = backup.Path;
        DeviceId = backup.DeviceId;
        Parent = backup.Parent ?? "-";
        SizeText = FormatSize(backup.Size);
        IsCurrentDeviceHead = backup.IsCurrentDeviceHead;
        Summary = $"{Date} | {SizeText} | device: {DeviceId}";
        HeadBadge = backup.IsCurrentDeviceHead ? "Current" : string.Empty;
    }

    public string Date { get; }

    public string Describe { get; }

    public string Path { get; }

    public string DeviceId { get; }

    public string Parent { get; }

    public string SizeText { get; }

    public bool IsCurrentDeviceHead { get; }

    public string Summary { get; }

    public string HeadBadge { get; }

    private static string FormatSize(long size)
    {
        if (size < 1024)
        {
            return $"{size} B";
        }

        if (size < 1024 * 1024)
        {
            return $"{size / 1024d:0.0} KB";
        }

        return $"{size / 1024d / 1024d:0.0} MB";
    }
}
