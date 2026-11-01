using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Maui.Storage;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services;
using EflayGameSaveManager.Core.Services.GamePlugins;
using EflayGameSaveManager.Maui.Services;
using Microsoft.Maui.Devices;

namespace EflayGameSaveManager.Maui.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private const string NavigationTabFavorites = "Favorites";
    private const string NavigationTabAllGames = "AllGames";
    private const string NavigationTabConfig = "Config";

    private readonly GameSaveManagerConfigurationService _configurationService;
    private readonly GameLibraryService _gameLibraryService;
    private readonly CloudSyncService _cloudSyncService;
    private readonly ArchiveTransferService _archiveTransferService;
    private readonly MobileWorkspaceService _workspaceService;
    private readonly FavoriteGamesService _favoriteGamesService;
    private readonly StoragePickerService _storagePickerService;
    private readonly MobileNfsMwSaveService _nfsEditor = new();

    public ObservableCollection<MobileNfsProfileChoice> NfsProfiles { get; } = [];
    [ObservableProperty] private MobileNfsProfileChoice? _selectedNfsProfile;
    [ObservableProperty] private bool _isNfs9Selected;
    [ObservableProperty] private bool _nfsProfileLoaded;
    [ObservableProperty] private string _nfsAlias = string.Empty;
    [ObservableProperty] private string _nfsMoney = string.Empty;
    [ObservableProperty] private string _nfsBounty = string.Empty;
    [ObservableProperty] private string _nfsStage = string.Empty;
    [ObservableProperty] private string _nfsStatus = "请选择并读取个人存档。";

    partial void OnSelectedNfsProfileChanged(MobileNfsProfileChoice? value) => NfsProfileLoaded = false;

    [RelayCommand]
    private async Task FindNfsProfilesAsync()
    {
        if (_currentConfig is null || _currentSnapshot is null || SelectedGame is null) return;
        var game = _currentConfig.Games.FirstOrDefault(g => g.Name == SelectedGame.Name);
        var gameName = SelectedGame.Name;
        var deviceId = _currentSnapshot.CurrentDevice.DeviceId;
        var paths = game?.SavePaths.Where(u => u.UnitType is SaveUnitType.Folder or SaveUnitType.File)
            .Select(u => u.Paths.GetValueOrDefault(deviceId)).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!).ToArray() ?? [];
        await RunBusyAsync("正在查找 NFS9 个人存档...", async () =>
        {
            var profiles = await _nfsEditor.ListAsync(paths);
            if (SelectedGame?.Name != gameName) return;
            NfsProfiles.Clear();
            foreach (var profile in profiles) NfsProfiles.Add(profile);
            SelectedNfsProfile = null;
            NfsStatus = profiles.Count == 0 ? "未找到可识别的 PC 版 NFS9 个人存档，请检查安卓路径。" : $"找到 {profiles.Count} 个存档，请选择后读取。";
        });
    }

    [RelayCommand]
    private async Task ReadNfsProfileAsync()
    {
        var selected = SelectedNfsProfile;
        if (selected is null) { NfsStatus = "请先选择个人存档。"; return; }
        await RunBusyAsync("正在读取个人存档...", async () =>
        {
            var profile = await _nfsEditor.ReadAsync(selected.SavePath);
            if (SelectedNfsProfile == selected) ShowNfsProfile(profile);
        });
    }

    [RelayCommand]
    private async Task SaveNfsProfileAsync()
    {
        var selected = SelectedNfsProfile;
        if (!NfsProfileLoaded || selected is null) return;
        if (!uint.TryParse(NfsMoney, out var money) || !ulong.TryParse(NfsBounty, out var bounty))
        { NfsStatus = "金钱和赏金请输入有效的非负整数。"; return; }
        await RunBusyAsync("正在备份并保存个人存档...", async () =>
        {
            var profile = await _nfsEditor.SaveAsync(selected.SavePath, NfsAlias, money, bounty);
            if (SelectedNfsProfile == selected) { ShowNfsProfile(profile); NfsStatus = "已保存，原存档已备份。"; }
        });
    }

    [RelayCommand]
    private async Task MakeNfsBossReadyAsync()
    {
        var selected = SelectedNfsProfile;
        if (!NfsProfileLoaded || selected is null) return;
        await RunBusyAsync("正在备份并设置黑名单挑战条件...", async () =>
        {
            var profile = await _nfsEditor.ApplyBossReadyAsync(selected.SavePath);
            if (SelectedNfsProfile == selected) { ShowNfsProfile(profile); NfsStatus = $"已套用黑名单 {NfsStage} 可挑战进度，原存档已备份。"; }
        });
    }

    private void ShowNfsProfile(NfsMwPersonalProfile profile)
    {
        NfsAlias = profile.Alias;
        NfsMoney = profile.Money.ToString();
        NfsBounty = profile.TotalBounty.ToString();
        NfsStage = profile.CurrentBlacklistStage is >= 1 and <= 15 ? $"#{profile.CurrentBlacklistStage}" : "未知";
        NfsStatus = $"当前存档：{profile.SavePath}";
        NfsProfileLoaded = true;
    }

    private ManagerConfig? _currentConfig;
    private AppSnapshot? _currentSnapshot;
    private string _configPath = string.Empty;
    private HashSet<string> _favoriteGameNames = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _statusMessage = "Preparing mobile save sync workspace...";

    [ObservableProperty]
    private string _configPathSummary = "Config: pending";

    [ObservableProperty]
    private string _currentDeviceSummary = "Device: pending";

    [ObservableProperty]
    private GameListItemViewModel? _selectedGame;

    [ObservableProperty]
    private GameListItemViewModel? _selectedFavoriteGame;

    [ObservableProperty]
    private string _selectedGameTitle = "Select a game";

    [ObservableProperty]
    private string _selectedGameSummary = "Cloud sync and backup details will show here.";

    [ObservableProperty]
    private string _selectedGameDetails = "No game selected.";

    [ObservableProperty]
    private string _cloudDetails = "Cloud status not loaded.";

    public ObservableCollection<CloudBackupChoice> CloudBackups { get; } = [];

    [ObservableProperty]
    private CloudBackupChoice? _selectedCloudBackup;

    [ObservableProperty]
    private string _cloudBackupDescription = string.Empty;

    public bool HasSelectedCloudBackup => SelectedCloudBackup is not null;

    partial void OnSelectedCloudBackupChanged(CloudBackupChoice? value)
    {
        CloudBackupDescription = value?.Backup.Describe ?? string.Empty;
        OnPropertyChanged(nameof(HasSelectedCloudBackup));
    }

    [RelayCommand]
    private async Task SaveCloudBackupDescriptionAsync()
    {
        var backup = SelectedCloudBackup?.Backup;
        if (backup is null || !TryGetSelectedCloudContext(out var game, out var snapshot, out var settings))
            return;
        var description = CloudBackupDescription;
        await RunBusyAsync("正在保存存档描述...", async () =>
        {
            await _cloudSyncService.UpdateGameBackupDescriptionAsync(game, settings, backup.Date, backup.DeviceId, description);
            if (SelectedGame?.Name == game.Name)
            {
                await RefreshCloudCoreAsync(game, snapshot, settings);
                SelectedCloudBackup = CloudBackups.FirstOrDefault(item => item.Backup.Date == backup.Date && item.Backup.DeviceId == backup.DeviceId);
            }
            StatusMessage = "存档描述已保存。";
        });
    }

    [ObservableProperty]
    private SaveUnitTargetOption? _selectedSaveUnitTarget;

    [ObservableProperty]
    private string _editSavePath = string.Empty;

    [ObservableProperty]
    private string _editGamePath = string.Empty;

    /// <summary>Semicolon-separated wildcards for device-local files (neither packed nor overwritten).</summary>
    [ObservableProperty]
    private string _editExcludePatterns = string.Empty;

    /// <summary>Folder units only: sync the folder's contents and ignore its own name.</summary>
    [ObservableProperty]
    private bool _editContentsOnly;

    [ObservableProperty]
    private bool _addContentsOnly;

    [ObservableProperty]
    private bool _editZipMode;

    public bool IsEditPathMode => !EditZipMode;
    public bool IsEditFolderMode => !EditZipMode && SelectedSaveUnitTarget?.UnitType == SaveUnitType.Folder;
    public bool IsEditContainerMode => EditZipMode || SelectedSaveUnitTarget?.UnitType == SaveUnitType.Folder;
    public bool IsAddContainerMode => SelectedSaveUnitType is "Folder" or "Zip";
    public string EditFilePickerText => EditZipMode ? "选择 ZIP 文件" : "Pick file";
    public string AddFilePickerText => IsZipSaveUnitTypeSelected ? "选择 ZIP 文件" : "Pick file";
    public bool AddZipMode { get => IsZipSaveUnitTypeSelected; set => SelectedSaveUnitType = value ? "Zip" : "Folder"; }

    partial void OnEditZipModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEditPathMode));
        OnPropertyChanged(nameof(IsEditFolderMode));
        OnPropertyChanged(nameof(IsEditContainerMode));
        OnPropertyChanged(nameof(EditFilePickerText));
    }

    partial void OnSelectedSaveUnitTargetChanged(SaveUnitTargetOption? value)
    {
        EditZipMode = value?.UnitType == SaveUnitType.Zip;
        OnPropertyChanged(nameof(IsEditFolderMode));
        OnPropertyChanged(nameof(IsEditContainerMode));
        if (value is null) return;
        var unit = TryGetSelectedGameDefinition(SelectedGame?.Name ?? "")?.SavePaths.FirstOrDefault(unit => unit.Id == value.SaveUnitId);
        EditSavePath = unit?.Paths.GetValueOrDefault(_currentSnapshot?.CurrentDevice.DeviceId ?? "") ?? "";
        EditExcludePatterns = SavePathFilter.FormatPatterns(unit?.Exclude);
        EditContentsOnly = unit?.ContentsOnly ?? false;
    }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isAddGameVisible;

    [ObservableProperty]
    private bool _isNavigationDrawerOpen;

    [ObservableProperty]
    private string _selectedNavigationTab = NavigationTabFavorites;

    [ObservableProperty]
    private string _configRawText = string.Empty;

    [ObservableProperty]
    private string _addGameName = string.Empty;

    [ObservableProperty]
    private string _addSavePath = string.Empty;

    [ObservableProperty]
    private string _addGamePath = string.Empty;

    [ObservableProperty]
    private string _selectedSaveUnitType = "Folder";

    [ObservableProperty]
    private bool _hasNoFavoriteGames = true;

    public MainPageViewModel(
        GameSaveManagerConfigurationService configurationService,
        GameLibraryService gameLibraryService,
        CloudSyncService cloudSyncService,
        ArchiveTransferService archiveTransferService,
        MobileWorkspaceService workspaceService,
        FavoriteGamesService favoriteGamesService,
        StoragePickerService storagePickerService)
    {
        _configurationService = configurationService;
        _gameLibraryService = gameLibraryService;
        _cloudSyncService = cloudSyncService;
        _archiveTransferService = archiveTransferService;
        _workspaceService = workspaceService;
        _favoriteGamesService = favoriteGamesService;
        _storagePickerService = storagePickerService;
    }

    public bool HasLoaded { get; private set; }

    public ObservableCollection<GameListItemViewModel> Games { get; } = [];

    public ObservableCollection<GameListItemViewModel> FavoriteGames { get; } = [];

    public ObservableCollection<SaveUnitTargetOption> SaveUnitTargets { get; } = [];

    public IReadOnlyList<string> SaveUnitTypeOptions { get; } = ["Folder", "File", "Zip"];

    public bool IsZipSaveUnitTypeSelected => string.Equals(SelectedSaveUnitType, "Zip", StringComparison.OrdinalIgnoreCase);

    public bool IsPathBasedSaveUnitTypeSelected => !IsZipSaveUnitTypeSelected;

    public bool IsFavoriteSelected => SelectedFavoriteGame is not null;

    public string SelectedTabTitle => SelectedNavigationTab switch
    {
        NavigationTabFavorites => "Favorites",
        NavigationTabConfig => "Config",
        _ => "All games"
    };

    public bool IsFavoritesTabSelected => string.Equals(SelectedNavigationTab, NavigationTabFavorites, StringComparison.Ordinal);

    public bool IsAllGamesTabSelected => string.Equals(SelectedNavigationTab, NavigationTabAllGames, StringComparison.Ordinal);

    public bool IsConfigTabSelected => string.Equals(SelectedNavigationTab, NavigationTabConfig, StringComparison.Ordinal);

    partial void OnSelectedGameChanged(GameListItemViewModel? value)
    {
        ApplySelectedGame(value);
    }

    partial void OnSelectedFavoriteGameChanged(GameListItemViewModel? value)
    {
        if (value is not null)
        {
            SelectedGame = value;
        }

        OnPropertyChanged(nameof(IsFavoriteSelected));
        OnPropertyChanged(nameof(SelectedGameTitle));
        OnPropertyChanged(nameof(SelectedGameSummary));
        OnPropertyChanged(nameof(CloudDetails));
    }

    partial void OnSelectedNavigationTabChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedTabTitle));
        OnPropertyChanged(nameof(IsFavoritesTabSelected));
        OnPropertyChanged(nameof(IsAllGamesTabSelected));
        OnPropertyChanged(nameof(IsConfigTabSelected));
    }

    partial void OnSelectedSaveUnitTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsZipSaveUnitTypeSelected));
        OnPropertyChanged(nameof(IsPathBasedSaveUnitTypeSelected));
        OnPropertyChanged(nameof(AddZipMode));
        OnPropertyChanged(nameof(AddFilePickerText));
        OnPropertyChanged(nameof(IsAddContainerMode));
    }

    public async Task InitializeAsync()
    {
        if (HasLoaded)
        {
            return;
        }

        await ReloadAsync();
        HasLoaded = true;
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        await RunBusyAsync(
            "Loading mobile configuration...",
            async () => { await ReloadCoreAsync(); });
    }

    private async Task ReloadCoreAsync()
    {
        _configPath = await _workspaceService.EnsureConfigPathAsync(_configurationService);
        _currentConfig = await _configurationService.LoadAsync(_configPath);
        _favoriteGameNames = new HashSet<string>(_favoriteGamesService.Load(), StringComparer.OrdinalIgnoreCase);

        var deviceName = $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}".Trim();
        _currentSnapshot = _gameLibraryService.CreateSnapshot(_currentConfig, _configPath, deviceName, DeviceInfo.Current.Platform.ToString());

        // Record this device's real platform so save-format decisions never rely on path shapes.
        if (_currentSnapshot.CurrentDevice.PlatformChanged)
        {
            await _configurationService.SaveAsync(_configPath, _currentConfig);
        }

        ConfigPathSummary = $"Config: {_configPath}";
        CurrentDeviceSummary =
            $"Device: {_currentSnapshot.CurrentDevice.DeviceName} [{_currentSnapshot.CurrentDevice.DeviceId}] | Platform: {DeviceInfo.Current.Platform}";

        var previousSelection = SelectedGame?.Name;
        var previousFavoriteSelection = SelectedFavoriteGame?.Name;
        Games.Clear();
        FavoriteGames.Clear();

        foreach (var game in _currentSnapshot.Games)
        {
            var item = new GameListItemViewModel(game, _favoriteGameNames.Contains(game.Name));
            Games.Add(item);
            if (item.IsFavorite)
            {
                FavoriteGames.Add(item);
            }
        }

        HasNoFavoriteGames = FavoriteGames.Count == 0;

        SelectedGame = Games.FirstOrDefault(item => string.Equals(item.Name, previousSelection, StringComparison.OrdinalIgnoreCase))
                       ?? Games.FirstOrDefault();
        if (IsFavoritesTabSelected || !string.IsNullOrWhiteSpace(previousFavoriteSelection))
        {
            RestoreFavoriteSelection(previousFavoriteSelection);
        }
        else
        {
            SelectedFavoriteGame = null;
        }

        StatusMessage = Games.Count == 0
            ? "No games found yet. Add the Android games you want to sync."
            : $"Loaded {Games.Count} games.";
    }

    [RelayCommand]
    private void ToggleAddGame()
    {
        IsAddGameVisible = !IsAddGameVisible;
    }

    [RelayCommand]
    private void ToggleNavigationDrawer()
    {
        IsNavigationDrawerOpen = !IsNavigationDrawerOpen;
    }

    [RelayCommand]
    private void CloseNavigationDrawer()
    {
        IsNavigationDrawerOpen = false;
    }

    [RelayCommand]
    private async Task SelectNavigationTabAsync(string? tab)
    {
        SelectedNavigationTab = tab switch
        {
            NavigationTabFavorites => NavigationTabFavorites,
            NavigationTabConfig => NavigationTabConfig,
            _ => NavigationTabAllGames
        };

        IsNavigationDrawerOpen = false;

        if (IsConfigTabSelected)
        {
            await LoadConfigTextAsync();
        }
        else if (IsFavoritesTabSelected)
        {
            RestoreFavoriteSelection(SelectedFavoriteGame?.Name ?? SelectedGame?.Name);
        }
    }

    [RelayCommand]
    private void SelectGame(GameListItemViewModel? game)
    {
        if (game is not null)
        {
            SelectedGame = game;
            SelectedNavigationTab = NavigationTabAllGames;
        }
    }

    [RelayCommand]
    private void SelectFavoriteGame(GameListItemViewModel? game)
    {
        if (game is null)
        {
            return;
        }

        SelectedFavoriteGame = FavoriteGames.FirstOrDefault(item =>
            string.Equals(item.Name, game.Name, StringComparison.OrdinalIgnoreCase)) ?? game;
        SelectedNavigationTab = NavigationTabFavorites;
    }

    private void RestoreFavoriteSelection(string? preferredGameName)
    {
        if (FavoriteGames.Count == 0)
        {
            SelectedFavoriteGame = null;
            return;
        }

        SelectedFavoriteGame = FavoriteGames.FirstOrDefault(item =>
                                   !string.IsNullOrWhiteSpace(preferredGameName) &&
                                   string.Equals(item.Name, preferredGameName, StringComparison.OrdinalIgnoreCase))
                               ?? FavoriteGames.FirstOrDefault(item =>
                                   SelectedGame is not null &&
                                   string.Equals(item.Name, SelectedGame.Name, StringComparison.OrdinalIgnoreCase))
                               ?? FavoriteGames.FirstOrDefault();
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(GameListItemViewModel? game)
    {
        var target = game ?? SelectedGame;
        if (target is null)
        {
            StatusMessage = "Select a game first.";
            return;
        }

        var isFavorite = _favoriteGameNames.Add(target.Name);
        if (!isFavorite)
        {
            _favoriteGameNames.Remove(target.Name);
        }

        // Let the button click complete before mutating bound collections.
        await Task.Yield();
        UpdateFavoriteState(target.Name, isFavorite);
        _favoriteGamesService.Save(_favoriteGameNames);
        StatusMessage = isFavorite
            ? $"Added {target.Name} to favorites."
            : $"Removed {target.Name} from favorites.";
    }

    private void UpdateFavoriteState(string gameName, bool isFavorite)
    {
        var gameItem = Games.FirstOrDefault(item =>
            string.Equals(item.Name, gameName, StringComparison.OrdinalIgnoreCase));
        if (gameItem is not null)
        {
            gameItem.IsFavorite = isFavorite;
        }

        var favoriteItem = FavoriteGames.FirstOrDefault(item =>
            string.Equals(item.Name, gameName, StringComparison.OrdinalIgnoreCase));

        if (isFavorite)
        {
            if (gameItem is not null && favoriteItem is null)
            {
                FavoriteGames.Add(gameItem);
            }

            if (IsFavoritesTabSelected)
            {
                SelectedFavoriteGame = FavoriteGames.FirstOrDefault(item =>
                    string.Equals(item.Name, gameName, StringComparison.OrdinalIgnoreCase))
                    ?? SelectedFavoriteGame;
            }
        }
        else
        {
            if (favoriteItem is not null)
            {
                FavoriteGames.Remove(favoriteItem);
            }

            if (SelectedFavoriteGame is not null &&
                string.Equals(SelectedFavoriteGame.Name, gameName, StringComparison.OrdinalIgnoreCase))
            {
                RestoreFavoriteSelection(SelectedGame?.Name);
            }
        }

        HasNoFavoriteGames = FavoriteGames.Count == 0;

        if (FavoriteGames.Count == 0)
        {
            SelectedFavoriteGame = null;
        }
    }

    [RelayCommand]
    private async Task RefreshCloudAsync()
    {
        if (!TryGetSelectedCloudContext(out var game, out var snapshot, out var cloudSettings))
        {
            return;
        }

        await RunBusyAsync(
            $"Refreshing cloud status for {game.Name}...",
            async () => { await RefreshCloudCoreAsync(game, snapshot, cloudSettings); });
    }

    private async Task RefreshCloudCoreAsync(GameSnapshot game, AppSnapshot snapshot, CloudSettings cloudSettings)
    {
        var status = await _cloudSyncService.GetGameCurrentStatusAsync(game, snapshot.CurrentDevice, cloudSettings);
        var backups = await _cloudSyncService.ListGameBackupsAsync(game, snapshot.CurrentDevice, cloudSettings);
        if (SelectedGame?.Name != game.Name) return;
        SelectedCloudBackup = null;
        CloudBackups.Clear();
        foreach (var backup in backups)
        {
            var deviceName = _currentConfig?.Devices.GetValueOrDefault(backup.DeviceId)?.Name ?? backup.DeviceId;
            CloudBackups.Add(new CloudBackupChoice(backup, deviceName));
        }
        SelectedCloudBackup = CloudBackups.FirstOrDefault();
        CloudDetails = BuildCloudDetails(status, backups);
        StatusMessage = $"Cloud status refreshed for {game.Name}.";
    }

    [RelayCommand]
    private async Task UploadCurrentAsync()
    {
        if (!TryGetSelectedCloudContext(out var game, out var snapshot, out var cloudSettings))
        {
            return;
        }

        if (GameHasZipUnits())
        {
            if (!CanUseSingleZipUnit()) return;
            var zipUnit = GetZipUnit();
            var configuredPath = GetConfiguredZipPath();
            var selection = string.IsNullOrWhiteSpace(configuredPath)
                ? await _storagePickerService.PickZipFileAsync()
                : StorageSelectionResult.Success(configuredPath, "");
            if (!selection.IsSuccess) { StatusMessage = selection.Message; return; }
            await RunBusyAsync($"Uploading {game.Name} zip save...", async () =>
            {
                var workRoot = Path.Combine(FileSystem.CacheDirectory, "zip-upload", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workRoot);
                try
                {
                    var archivePath = Path.Combine(workRoot, "save.zip");
                    await _storagePickerService.CopyConfiguredZipToLocalAsync(selection.Path, archivePath);
                    using (var zip = System.IO.Compression.ZipFile.OpenRead(archivePath)) _ = zip.Entries.Count;
                    var uploadArchive = ZipSaveLayoutService.CreateUploadArchive(archivePath, workRoot,
                        zipUnit?.Id ?? 0, zipUnit?.ContentsOnly ?? false);
                    await _cloudSyncService.UploadGameCurrentSaveAsync(game, snapshot.CurrentDevice, cloudSettings, uploadArchive);
                    await RefreshCloudCoreAsync(game, snapshot, cloudSettings);
                    StatusMessage = "ZIP 备份上传成功。";
                }
                finally { if (Directory.Exists(workRoot)) Directory.Delete(workRoot, true); }
            });
            return;
        }

        if (!HasCurrentDevicePaths(game.Name, snapshot.CurrentDevice.DeviceId))
        {
            StatusMessage = "No save paths configured for this device. Edit the game to add Android save paths first.";
            return;
        }

        await RunBusyAsync(
            $"Uploading {game.Name} current save...",
            async () =>
            {
#if ANDROID
                var staged = await StageRestrictedPathsForUploadAsync(game.Name, snapshot.CurrentDevice.DeviceId);
#endif
                try
                {
                    // Rebuild snapshot with potentially redirected paths
                    var uploadSnapshot = _currentConfig is not null
                        ? _gameLibraryService.CreateSnapshot(_currentConfig, _configPath, snapshot.CurrentDevice.DeviceName, DeviceInfo.Current.Platform.ToString())
                        : snapshot;

                    var uploadGame = AndroidGameSnapshotFactory.Create(
                        uploadSnapshot.Games.First(g => g.Name == game.Name),
                        TryGetSelectedGameDefinition(game.Name)!, snapshot.CurrentDevice.DeviceId);
                    var result = await _cloudSyncService.UploadGameCurrentSaveAsync(
                        uploadGame,
                        snapshot.CurrentDevice, cloudSettings);
                    StatusMessage = $"Uploaded current save to {result.RootKey}.";
                    await RefreshCloudCoreAsync(game, snapshot, cloudSettings);
                }
                finally
                {
#if ANDROID
                    RestoreOriginalSavePaths(game.Name, snapshot.CurrentDevice.DeviceId, staged.OriginalPaths);
                    CleanupTempDirs(staged.TempDirs);
#endif
                }
            });
    }

    [RelayCommand]
    private async Task RestoreCurrentAsync()
    {
        if (!TryGetSelectedCloudContext(out var game, out var snapshot, out var cloudSettings))
        {
            return;
        }

        if (GameHasZipUnits())
        {
            if (!CanUseSingleZipUnit()) return;
            var zipUnit = GetZipUnit();
            var configuredZipPath = GetConfiguredZipPath();
            await RunBusyAsync(
                $"Downloading {game.Name} latest zip backup...",
                async () =>
                {
                    var latestBackup = (await _cloudSyncService.ListGameBackupsAsync(game, snapshot.CurrentDevice, cloudSettings))
                        .OrderByDescending(item => item.Date, StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (latestBackup is null)
                    {
                        StatusMessage = $"No cloud backups found for {game.Name}.";
                        return;
                    }

                    var workRoot = Path.Combine(FileSystem.CacheDirectory, "zip-restore", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(workRoot);
                    try
                    {
                        var downloadedArchivePath = Path.Combine(workRoot, $"{game.Name}-{latestBackup.Date}.zip");
                        var result = await _cloudSyncService.DownloadGameBackupArchiveAsync(
                            game,
                            cloudSettings,
                            latestBackup.Date,
                            latestBackup.DeviceId,
                            downloadedArchivePath,
                            overwrite: true);

                        var normalizedArchivePath = _archiveTransferService.NormalizeZipArchiveForMobileRestore(downloadedArchivePath, workRoot,
                            zipUnit?.ContentsOnly ?? false, zipUnit?.Id, game.Name);
                        var configuredPath = configuredZipPath;
                        if (!string.IsNullOrWhiteSpace(configuredPath))
                        {
                            await _storagePickerService.WriteConfiguredZipAsync(normalizedArchivePath, configuredPath);
                            StatusMessage = "ZIP 已恢复到原文件，旧 ZIP 已保留备份。";
                        }
                        else
                        {
                            await using var archiveStream = File.OpenRead(normalizedArchivePath);
                            var saveResult = await _storagePickerService.SaveFileAsync($"{game.Name}.zip", archiveStream);
                            StatusMessage = saveResult.Message;
                        }
                    }
                    finally
                    {
                        if (Directory.Exists(workRoot))
                        {
                            Directory.Delete(workRoot, true);
                        }
                    }

                    await RefreshCloudCoreAsync(game, snapshot, cloudSettings);
                });
            return;
        }

        if (!HasCurrentDevicePaths(game.Name, snapshot.CurrentDevice.DeviceId))
        {
            StatusMessage = "No save paths configured for this device. Edit the game to add Android save paths first.";
            return;
        }

        await RunBusyAsync(
            $"Restoring {game.Name} latest cloud save...",
            async () =>
            {
                // Resolve the encoding platform from the REAL device paths before any
                // redirect: the restricted-path redirect below swaps the unit path for a
                // cache folder, which would otherwise be detected as a desktop target and
                // make the restore write PC-format .json files the Android port cannot read.
                var targetPlatform = GameSaveFormatPlugins
                    .ForGame(game)?
                    .ResolveTarget(game, snapshot.CurrentDevice);

#if ANDROID
                var redirectMap = await RedirectRestrictedPathsForRestoreAsync(game.Name, snapshot.CurrentDevice.DeviceId);
#endif
                try
                {
                    var restoreSnapshot = _currentConfig is not null
                        ? _gameLibraryService.CreateSnapshot(_currentConfig, _configPath, snapshot.CurrentDevice.DeviceName, DeviceInfo.Current.Platform.ToString())
                        : snapshot;

                    var restoreGame = AndroidGameSnapshotFactory.Create(
                        restoreSnapshot.Games.First(g => g.Name == game.Name),
                        TryGetSelectedGameDefinition(game.Name)!, snapshot.CurrentDevice.DeviceId);
                    var result = await _cloudSyncService.RestoreGameLatestSaveAsync(
                        restoreGame,
                        snapshot.CurrentDevice, cloudSettings,
                        targetPlatformOverride: targetPlatform);
                    StatusMessage = $"Restored latest cloud save from {result.RootKey}.";
#if ANDROID
                    await ApplyRestoredFilesToRestrictedPathsAsync(redirectMap);
#endif
                    await RefreshCloudCoreAsync(game, snapshot, cloudSettings);
                }
                finally
                {
#if ANDROID
                    RestoreOriginalSavePaths(game.Name, snapshot.CurrentDevice.DeviceId,
                        redirectMap.ToDictionary(item => item.SaveUnitId, item => item.OriginalPath));
                    CleanupRedirectMap(redirectMap);
#endif
                }
            });
    }

#if ANDROID
    private static bool IsRestrictedPath(string? path)
    {
        if (path is null) return false;
        // Android 11+ restricted directories — needs Shizuku to access
        return path.StartsWith("/data/data/") ||
               path.StartsWith("/data/user/") ||
               path.Contains("/Android/data/") ||
               path.Contains("/Android/obb/");
    }

    private sealed record StagedUploadPaths(List<string> TempDirs, Dictionary<int, string> OriginalPaths);

    private async Task<StagedUploadPaths> StageRestrictedPathsForUploadAsync(string gameName, string deviceId)
    {
        var tempDirs = new List<string>();
        var originals = new Dictionary<int, string>();
        var gameDef = _currentConfig?.Games.FirstOrDefault(g =>
            string.Equals(g.Name, gameName, StringComparison.OrdinalIgnoreCase));
        if (gameDef is null || _currentConfig is null) return new StagedUploadPaths(tempDirs, originals);

        var stagingRoot = Path.Combine(FileSystem.CacheDirectory, "shizuku-upload", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        tempDirs.Add(stagingRoot);

        try
        {
            foreach (var unit in gameDef.SavePaths)
            {
                if (!unit.Paths.TryGetValue(deviceId, out var originalPath) ||
                    string.IsNullOrWhiteSpace(originalPath) ||
                    !IsRestrictedPath(originalPath))
                    continue;

                var stagedPath = await Platforms.Android.ShizukuFileService.CopyFromRestrictedAsync(
                    originalPath, Path.Combine(stagingRoot, $"unit-{unit.Id}"));
                if (stagedPath is null)
                {
                    throw new IOException(
                        $"备份失败：无法读取受限存档目录 {originalPath}。" +
                        Platforms.Android.ShizukuFileService.StatusMessage);
                }

                originals[unit.Id] = originalPath;
                unit.Paths[deviceId] = stagedPath;
            }

            return new StagedUploadPaths(tempDirs, originals);
        }
        catch
        {
            RestoreOriginalSavePaths(gameName, deviceId, originals);
            CleanupTempDirs(tempDirs);
            throw;
        }
    }

    private void RestoreOriginalSavePaths(string gameName, string deviceId, Dictionary<int, string> originals)
    {
        if (originals.Count == 0) return;
        var gameDef = _currentConfig?.Games.FirstOrDefault(g =>
            string.Equals(g.Name, gameName, StringComparison.OrdinalIgnoreCase));
        if (gameDef is null) return;

        foreach (var unit in gameDef.SavePaths)
        {
            if (originals.TryGetValue(unit.Id, out var originalPath))
            {
                unit.Paths[deviceId] = originalPath;
            }
        }
    }

    private sealed record RestoreRedirect(string OriginalPath, string TempDir, string DeviceId, int SaveUnitId);

    private async Task<List<RestoreRedirect>> RedirectRestrictedPathsForRestoreAsync(string gameName, string deviceId)
    {
        var redirects = new List<RestoreRedirect>();
        var gameDef = _currentConfig?.Games.FirstOrDefault(g =>
            string.Equals(g.Name, gameName, StringComparison.OrdinalIgnoreCase));
        if (gameDef is null) return redirects;

        var stagingRoot = Path.Combine(FileSystem.CacheDirectory, "shizuku-restore", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);

        foreach (var unit in gameDef.SavePaths)
        {
            if (!unit.Paths.TryGetValue(deviceId, out var originalPath) ||
                string.IsNullOrWhiteSpace(originalPath) ||
                !IsRestrictedPath(originalPath))
                continue;

            // Preserve the original basename so folder-mode wrapper matching still
            // works before the staged files are merged back into the real directory.
            var tempDir = AndroidGameSnapshotFactory.GetRestoreStagingPath(
                stagingRoot, unit.Id, unit.UnitType, originalPath);
            unit.Paths[deviceId] = tempDir;
            redirects.Add(new RestoreRedirect(originalPath, tempDir, deviceId, unit.Id));
        }

        return redirects;
    }

    private static async Task ApplyRestoredFilesToRestrictedPathsAsync(List<RestoreRedirect> redirects)
    {
        var failures = new List<string>();
        foreach (var r in redirects)
        {
            if (!Directory.Exists(r.TempDir) && !File.Exists(r.TempDir))
            {
                continue;
            }

            var copied = await Platforms.Android.ShizukuFileService.CopyToRestrictedAsync(r.TempDir, r.OriginalPath);
            if (!copied)
            {
                failures.Add(r.OriginalPath);
            }
            else if (Directory.Exists(r.TempDir))
            {
                var retained = await Platforms.Android.ShizukuFileService.ArchiveDuplicateRestoreWrapperAsync(r.TempDir, r.OriginalPath);
                if (retained is not null) AppLogger.Info($"Retained duplicate restore wrapper: {retained}");
            }
        }

        if (failures.Count > 0)
        {
            // Never report success when the restricted write silently failed.
            throw new InvalidOperationException(
                "恢复失败：无法写入受限目录 " + string.Join("；", failures) +
                "。当前 Shizuku 状态：" + Platforms.Android.ShizukuFileService.StatusMessage +
                "。请打开 Shizuku 应用确认已启动并授权本应用后重试。");
        }
    }

    private static void CleanupTempDirs(List<string> tempDirs)
    {
        foreach (var d in tempDirs)
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, true); }
            catch { /* best effort */ }
        }
    }

    private static void CleanupRedirectMap(List<RestoreRedirect> redirects)
    {
        foreach (var r in redirects)
        {
            try { if (Directory.Exists(r.TempDir)) Directory.Delete(r.TempDir, true); }
            catch { /* best effort */ }
        }
    }
#endif

    private bool HasCurrentDevicePaths(string gameName, string deviceId)
    {
        var gameDefinition = _currentConfig?.Games.FirstOrDefault(g =>
            string.Equals(g.Name, gameName, StringComparison.OrdinalIgnoreCase));
        return gameDefinition?.SavePaths.Any(unit => unit.Paths.ContainsKey(deviceId)) == true;
    }

    [RelayCommand]
    private async Task AddGameAsync()
    {
        if (_currentConfig is null || _currentSnapshot is null)
        {
            StatusMessage = "Configuration is not loaded yet.";
            return;
        }

        var gameName = AddGameName.Trim();
        var savePath = AddSavePath.Trim();
        var gamePath = AddGamePath.Trim();

        if (string.IsNullOrWhiteSpace(gameName))
        {
            StatusMessage = "Game name is required.";
            return;
        }

        var isZipMode = IsZipSaveUnitTypeSelected;
        if (isZipMode && !IsZipSelectionPath(savePath))
        {
            StatusMessage = "ZIP 模式需要选择 .zip 文件，也可用自定义路径填写完整 ZIP 路径。";
            return;
        }
        var existingGame = _currentConfig.Games.FirstOrDefault(game =>
            string.Equals(game.Name, gameName, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(savePath))
        {
            StatusMessage = "Save path is required.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(savePath) && !IsSupportedAndroidPath(savePath))
        {
            StatusMessage = "Android save path must be an absolute path or content:// URI.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(gamePath) && !IsSupportedAndroidPath(gamePath))
        {
            StatusMessage = "Android game path must be an absolute path or content:// URI.";
            return;
        }

        if (!isZipMode && existingGame is not null)
        {
            StatusMessage = $"Game already exists: {gameName}";
            return;
        }

        var saveUnitType = SelectedSaveUnitType switch
        {
            "File" => SaveUnitType.File,
            "Zip" => SaveUnitType.Zip,
            _ => SaveUnitType.Folder
        };

        await RunBusyAsync(
            $"Adding {gameName}...",
            async () =>
            {
                if (isZipMode && existingGame is not null)
                {
                    var currentUnit = existingGame.SavePaths.FirstOrDefault(unit => unit.Paths.ContainsKey(_currentSnapshot.CurrentDevice.DeviceId));
                    MobileSaveUnitEditor.Apply(existingGame, _currentSnapshot.CurrentDevice.DeviceId, currentUnit?.Id,
                        SaveUnitType.Zip, savePath, [], AddContentsOnly);
                }
                else
                {
                    _currentConfig.Games.Add(new GameDefinition
                    {
                        Name = gameName,
                        SavePaths =
                        {
                            new SaveUnitDefinition
                            {
                                Id = 0,
                                UnitType = saveUnitType,
                                ContentsOnly = saveUnitType is SaveUnitType.Folder or SaveUnitType.Zip && AddContentsOnly,
                                DeleteBeforeApply = false,
                                Paths = string.IsNullOrWhiteSpace(savePath)
                                    ? []
                                    : new Dictionary<string, string>
                                    {
                                        [_currentSnapshot.CurrentDevice.DeviceId] = savePath
                                    }
                            }
                        },
                        GamePaths = string.IsNullOrWhiteSpace(gamePath)
                            ? []
                            : new Dictionary<string, string>
                            {
                                [_currentSnapshot.CurrentDevice.DeviceId] = gamePath
                            },
                        NextSaveUnitId = 1,
                        CloudSyncEnabled = true
                    });
                }

                await _configurationService.SaveAsync(_configPath, _currentConfig);
                AddGameName = string.Empty;
                AddSavePath = string.Empty;
                AddGamePath = string.Empty;
                AddContentsOnly = false;
                SelectedSaveUnitType = "Folder";
                IsAddGameVisible = false;
                await ReloadAsync();
                SelectedGame = Games.FirstOrDefault(item => string.Equals(item.Name, gameName, StringComparison.OrdinalIgnoreCase));
                StatusMessage = isZipMode && existingGame is not null
                    ? $"Added Zip sync unit to {gameName}"
                    : $"Added game: {gameName}";
            });
    }

    [RelayCommand]
    private async Task PickZipFileForNewGameAsync()
    {
        var selection = await _storagePickerService.PickConfiguredZipAsync();
        if (!selection.IsSuccess)
        {
            StatusMessage = selection.Message;
            return;
        }

        SelectedSaveUnitType = "Zip";
        AddSavePath = selection.Path;
        StatusMessage = selection.Message;
    }

    private bool GameHasZipUnits()
    {
        var definition = TryGetSelectedGameDefinition(SelectedGame?.Name ?? "");
        var deviceId = _currentSnapshot?.CurrentDevice.DeviceId ?? "";
        return MobileSaveUnitEditor.FindZip(definition, deviceId) is not null ||
            definition is not null && !definition.SavePaths.Any(unit => unit.Paths.ContainsKey(deviceId)) &&
            definition.SavePaths.Any(unit => unit.UnitType == SaveUnitType.Zip);
    }

    private string? GetConfiguredZipPath()
    {
        var deviceId = _currentSnapshot?.CurrentDevice.DeviceId ?? "";
        return MobileSaveUnitEditor.FindZip(TryGetSelectedGameDefinition(SelectedGame?.Name ?? ""), deviceId)?.Paths.GetValueOrDefault(deviceId);
    }

    private SaveUnitDefinition? GetZipUnit()
    {
        var definition = TryGetSelectedGameDefinition(SelectedGame?.Name ?? "");
        return MobileSaveUnitEditor.FindZip(definition, _currentSnapshot?.CurrentDevice.DeviceId ?? "")
            ?? definition?.SavePaths.FirstOrDefault(unit => unit.UnitType == SaveUnitType.Zip && unit.Paths.Count == 0);
    }

    private bool CanUseSingleZipUnit()
    {
        var id = _currentSnapshot?.CurrentDevice.DeviceId ?? "";
        var count = TryGetSelectedGameDefinition(SelectedGame?.Name ?? "")?.SavePaths
            .Count(unit => unit.Paths.TryGetValue(id, out var path) && !string.IsNullOrWhiteSpace(path)) ?? 0;
        if (count <= 1) return true;
        StatusMessage = "当前设备的 ZIP 模式只能配置一个 ZIP 存档单元，请先移除其他重复的本设备路径。";
        return false;
    }

    [RelayCommand]
    private async Task PickSaveFileAsync()
    {
        var zipMode = IsZipSaveUnitTypeSelected;
        var selection = zipMode ? await _storagePickerService.PickConfiguredZipAsync() : await _storagePickerService.PickFileAsync();
        if (!selection.IsSuccess)
        {
            StatusMessage = selection.Message;
            return;
        }

        SelectedSaveUnitType = zipMode ? "Zip" : "File";
        AddSavePath = selection.Path;
        StatusMessage = selection.Message;
    }

    [RelayCommand]
    private async Task PickSaveFolderAsync()
    {
        var selection = await _storagePickerService.PickFolderAsync();
        if (!selection.IsSuccess)
        {
            StatusMessage = selection.Message;
            return;
        }

        SelectedSaveUnitType = "Folder";
        AddSavePath = selection.Path;
        StatusMessage = selection.Message;
    }

    [RelayCommand]
    private async Task PickSelectedSaveFileAsync()
    {
        var selection = EditZipMode ? await _storagePickerService.PickConfiguredZipAsync() : await _storagePickerService.PickFileAsync();
        if (!selection.IsSuccess)
        {
            StatusMessage = selection.Message;
            return;
        }

        if (!EditZipMode && SelectedSaveUnitTarget?.IsNew == true)
        {
            SelectedSaveUnitTarget = SaveUnitTargets.FirstOrDefault(option => option.IsNew && option.UnitType == SaveUnitType.File)
                                     ?? SelectedSaveUnitTarget;
        }

        EditSavePath = selection.Path;
        StatusMessage = selection.Message;
    }

    [RelayCommand]
    private async Task PickSelectedSaveFolderAsync()
    {
        var selection = await _storagePickerService.PickFolderAsync();
        if (!selection.IsSuccess)
        {
            StatusMessage = selection.Message;
            return;
        }

        if (SelectedSaveUnitTarget?.IsNew == true)
        {
            SelectedSaveUnitTarget = SaveUnitTargets.FirstOrDefault(option => option.IsNew && option.UnitType == SaveUnitType.Folder)
                                     ?? SelectedSaveUnitTarget;
        }

        EditSavePath = selection.Path;
        StatusMessage = selection.Message;
    }

    [RelayCommand]
    private async Task PickCustomSavePathAsync()
    {
        var result = await _storagePickerService.PickCustomPathAsync(AddSavePath, SelectedSaveUnitType == "Folder", IsZipSaveUnitTypeSelected);
        if (result.IsSuccess) AddSavePath = result.Path;
        StatusMessage = result.Message;
    }

    [RelayCommand]
    private async Task PickSelectedCustomSavePathAsync()
    {
        var result = await _storagePickerService.PickCustomPathAsync(EditSavePath, IsEditFolderMode, EditZipMode);
        if (result.IsSuccess) EditSavePath = result.Path;
        StatusMessage = result.Message;
    }

    [RelayCommand]
    private async Task SaveSelectedGamePathsAsync()
    {
        if (_currentConfig is null || _currentSnapshot is null || SelectedGame is null)
        {
            StatusMessage = "Select a game first.";
            return;
        }

        if (SelectedSaveUnitTarget is null)
        {
            StatusMessage = "Pick a save unit first.";
            return;
        }

        var savePath = EditSavePath.Trim();
        var gamePath = EditGamePath.Trim();
        if (EditZipMode && !IsZipSelectionPath(savePath))
        {
            StatusMessage = "ZIP 模式需要选择 .zip 文件，也可用自定义路径填写完整 ZIP 路径。";
            return;
        }
        if (string.IsNullOrWhiteSpace(savePath))
        {
            StatusMessage = "Save path is required.";
            return;
        }

        if (!IsSupportedAndroidPath(savePath))
        {
            StatusMessage = "Android save path must be an absolute path or content:// URI.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(gamePath) && !IsSupportedAndroidPath(gamePath))
        {
            StatusMessage = "Android game path must be an absolute path or content:// URI.";
            return;
        }

        await RunBusyAsync(
            $"Saving Android paths for {SelectedGame.Name}...",
            async () =>
            {
                var game = _currentConfig.Games.FirstOrDefault(item =>
                    string.Equals(item.Name, SelectedGame.Name, StringComparison.OrdinalIgnoreCase));

                if (game is null)
                {
                    throw new InvalidOperationException($"Game not found: {SelectedGame.Name}");
                }

                var deviceId = _currentSnapshot.CurrentDevice.DeviceId;
                var type = EditZipMode ? SaveUnitType.Zip :
                    SelectedSaveUnitTarget.UnitType == SaveUnitType.Zip ? SaveUnitType.File : SelectedSaveUnitTarget.UnitType;
                MobileSaveUnitEditor.Apply(game, deviceId, SelectedSaveUnitTarget.SaveUnitId, type,
                    savePath, SavePathFilter.ParsePatterns(EditExcludePatterns), EditContentsOnly);

                if (string.IsNullOrWhiteSpace(gamePath))
                {
                    game.GamePaths.Remove(deviceId);
                }
                else
                {
                    game.GamePaths[deviceId] = gamePath;
                }

                await _configurationService.SaveAsync(_configPath, _currentConfig);
                await ReloadCoreAsync();
                SelectedGame = Games.FirstOrDefault(item => string.Equals(item.Name, game.Name, StringComparison.OrdinalIgnoreCase));
                StatusMessage = $"Updated Android paths for {game.Name}.";
            });
    }

    [RelayCommand]
    private async Task LoadConfigTextAsync()
    {
        if (string.IsNullOrWhiteSpace(_configPath) || !File.Exists(_configPath))
        {
            ConfigRawText = "{}";
            StatusMessage = "Configuration is not loaded yet.";
            return;
        }

        try
        {
            ConfigRawText = await File.ReadAllTextAsync(_configPath);
            StatusMessage = "Config loaded.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load config: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportConfigAsync()
    {
        if (string.IsNullOrWhiteSpace(_configPath) || !File.Exists(_configPath))
        { StatusMessage = "当前配置文件不存在。"; return; }
        try
        {
            await using var stream = File.OpenRead(_configPath);
            var result = await FileSaver.Default.SaveAsync("GameSaveManager.config.json", stream, CancellationToken.None);
            StatusMessage = result.IsSuccessful ? "配置已导出，可在 Kotlin 版导入。" : result.IsCancelled ? "已取消导出配置。" : $"导出配置失败：{result.Exception?.Message}";
        }
        catch (Exception ex) { StatusMessage = $"导出配置失败：{ex.Message}"; }
    }

    [RelayCommand]
    private async Task SaveConfigTextAsync()
    {
        if (string.IsNullOrWhiteSpace(_configPath))
        {
            StatusMessage = "Configuration is not loaded yet.";
            return;
        }

        await RunBusyAsync(
            "Saving config...",
            async () =>
            {
                var validationPath = Path.Combine(FileSystem.CacheDirectory, $"config-validation-{Guid.NewGuid():N}.json");
                try
                {
                    await File.WriteAllTextAsync(validationPath, ConfigRawText);
                    await _configurationService.LoadAsync(validationPath);
                }
                finally
                {
                    try
                    {
                        if (File.Exists(validationPath))
                        {
                            File.Delete(validationPath);
                        }
                    }
                    catch
                    {
                        // Best effort cleanup.
                    }
                }

                await File.WriteAllTextAsync(_configPath, ConfigRawText);
                _currentConfig = await _configurationService.LoadAsync(_configPath);
                await ReloadCoreAsync();
                await LoadConfigTextAsync();
                StatusMessage = "Config saved and reloaded.";
            });
    }

    private void ApplySelectedGame(GameListItemViewModel? item)
    {
        IsNfs9Selected = item is not null && (string.Equals(item.Name, "NFS9", StringComparison.OrdinalIgnoreCase) || item.Name.Contains("极品飞车9", StringComparison.OrdinalIgnoreCase) || item.Name.Contains("NFS MW 2005", StringComparison.OrdinalIgnoreCase) || (TryGetSelectedGameDefinition(item.Name)?.ChineseName.Contains("极品飞车9", StringComparison.OrdinalIgnoreCase) ?? false));
        NfsProfiles.Clear(); SelectedNfsProfile = null; NfsProfileLoaded = false; NfsStage = string.Empty;
        SelectedCloudBackup = null;
        CloudBackups.Clear();
        if (item is null)
        {
            SelectedGameTitle = "Select a game";
            SelectedGameSummary = "Cloud sync and backup details will show here.";
            SelectedGameDetails = "No game selected.";
            CloudDetails = "Cloud status not loaded.";
            SaveUnitTargets.Clear();
            SelectedSaveUnitTarget = null;
            EditSavePath = string.Empty;
            EditGamePath = string.Empty;
            EditExcludePatterns = string.Empty;
            EditContentsOnly = false;
            return;
        }

        SelectedGameTitle = item.Name;
        SelectedGameSummary = item.Summary;
        SelectedGameDetails = BuildGameDetails(item.Game, TryGetSelectedGameDefinition(item.Name), _currentSnapshot?.CurrentDevice.DeviceId);
        CloudDetails = "Cloud status not loaded.";
        LoadSelectedGameEditor(item.Game);
    }

    private void LoadSelectedGameEditor(GameSnapshot game)
    {
        SaveUnitTargets.Clear();

        foreach (var saveUnit in game.SaveUnits.OrderBy(unit => unit.Id))
        {
            SaveUnitTargets.Add(SaveUnitTargetOption.Existing(
                saveUnit.Id,
                saveUnit.UnitType,
                $"Unit {saveUnit.Id} [{saveUnit.UnitType}]"));
        }

        SaveUnitTargets.Add(SaveUnitTargetOption.New(SaveUnitType.Folder, "New folder save unit"));
        SaveUnitTargets.Add(SaveUnitTargetOption.New(SaveUnitType.File, "New file save unit"));
        SaveUnitTargets.Add(SaveUnitTargetOption.New(SaveUnitType.Zip, "New ZIP save unit"));

        var selectedGameDefinition = TryGetSelectedGameDefinition(game.Name);
        var deviceId = _currentSnapshot?.CurrentDevice.DeviceId;
        EditGamePath = GetExplicitCurrentDeviceGamePath(selectedGameDefinition, deviceId);

        var currentDevicePath = GetExplicitCurrentDevicePath(selectedGameDefinition, deviceId);
        if (currentDevicePath is not null)
        {
            SelectedSaveUnitTarget = SaveUnitTargets.FirstOrDefault(option =>
                !option.IsNew && option.SaveUnitId == currentDevicePath.SaveUnitId);
            EditSavePath = currentDevicePath.Path;
            EditExcludePatterns = SavePathFilter.FormatPatterns(
                selectedGameDefinition?.SavePaths
                    .FirstOrDefault(unit => unit.Id == currentDevicePath.SaveUnitId)?.Exclude);
            EditContentsOnly = selectedGameDefinition?.SavePaths
                .FirstOrDefault(unit => unit.Id == currentDevicePath.SaveUnitId)?.ContentsOnly ?? false;
            return;
        }

        SelectedSaveUnitTarget = SaveUnitTargets.FirstOrDefault(option => !option.IsNew)
                                 ?? SaveUnitTargets.FirstOrDefault();
        EditSavePath = string.Empty;
        EditExcludePatterns = string.Empty;
        EditContentsOnly = false;
    }

    private GameDefinition? TryGetSelectedGameDefinition(string gameName)
    {
        return _currentConfig?.Games.FirstOrDefault(game =>
            string.Equals(game.Name, gameName, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryGetSelectedCloudContext(
        out GameSnapshot game,
        out AppSnapshot snapshot,
        out CloudSettings cloudSettings)
    {
        game = default!;
        snapshot = default!;
        cloudSettings = default!;

        if (SelectedGame is null || _currentSnapshot is null || _currentConfig is null)
        {
            StatusMessage = "Select a game first.";
            return false;
        }

        cloudSettings = _currentConfig.Settings.CloudSettings;
        if (!string.Equals(cloudSettings.Backend.Type, "S3", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(cloudSettings.Backend.Endpoint) ||
            string.IsNullOrWhiteSpace(cloudSettings.Backend.Bucket) ||
            string.IsNullOrWhiteSpace(cloudSettings.Backend.AccessKeyId) ||
            string.IsNullOrWhiteSpace(cloudSettings.Backend.SecretAccessKey))
        {
            StatusMessage = "Cloud backend configuration is incomplete.";
            return false;
        }

        var selectedGameDefinition = TryGetSelectedGameDefinition(SelectedGame.Name)
                                   ?? throw new InvalidOperationException($"Game not found: {SelectedGame.Name}");
        game = AndroidGameSnapshotFactory.Create(SelectedGame.Game, selectedGameDefinition, _currentSnapshot.CurrentDevice.DeviceId);
        snapshot = _currentSnapshot;
        return true;
    }

    private async Task RunBusyAsync(string statusMessage, Func<Task> action)
    {
        if (IsBusy)
        {
            StatusMessage = "Another operation is still running.";
            return;
        }

        IsBusy = true;
        StatusMessage = statusMessage;

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            AppLogger.Error(statusMessage, ex);
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BuildGameDetails(GameSnapshot game, GameDefinition? gameDefinition, string? currentDeviceId)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Game: {game.Name}");
        builder.AppendLine($"Cloud sync enabled: {game.CloudSyncEnabled}");

        var currentDeviceGamePath = GetExplicitCurrentDeviceGamePath(gameDefinition, currentDeviceId);
        builder.AppendLine($"Android game path: {DisplayValue(currentDeviceGamePath)}");
        builder.AppendLine();
        builder.AppendLine("Android save paths:");

        var currentDevicePaths = GetExplicitCurrentDevicePaths(gameDefinition, currentDeviceId);
        if (currentDevicePaths.Count == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (var path in currentDevicePaths)
            {
                builder.AppendLine($"- unit {path.SaveUnitId} [{path.UnitType}] {DisplayValue(path.Path)}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private string BuildCloudDetails(GameCloudStatus status, IReadOnlyList<CloudGameBackup> backups)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Game: {status.GameName}");
        builder.AppendLine($"Cloud root: {status.RootKey}");
        builder.AppendLine($"Cloud data exists: {status.IsAvailable}");
        builder.AppendLine($"Current head: {DisplayValue(status.CurrentHead)}");
        builder.AppendLine($"Backup count: {status.BackupCount}");
        if (backups.Count == 0) builder.AppendLine("暂无云端存档。");

        return builder.ToString().TrimEnd();
    }

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

    private static string DisplayValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(empty)" : value;
    }

    private static IReadOnlyList<CurrentDevicePathInfo> GetExplicitCurrentDevicePaths(GameDefinition? gameDefinition, string? currentDeviceId)
    {
        if (gameDefinition is null || string.IsNullOrWhiteSpace(currentDeviceId))
        {
            return [];
        }

        return gameDefinition.SavePaths
            .Where(unit => unit.Paths.TryGetValue(currentDeviceId, out var path) && !string.IsNullOrWhiteSpace(path))
            .Select(unit => new CurrentDevicePathInfo(
                unit.Id,
                unit.UnitType,
                unit.Paths[currentDeviceId],
                unit.DeleteBeforeApply))
            .OrderBy(path => path.SaveUnitId)
            .ToArray();
    }

    private static CurrentDevicePathInfo? GetExplicitCurrentDevicePath(GameDefinition? gameDefinition, string? currentDeviceId)
    {
        return GetExplicitCurrentDevicePaths(gameDefinition, currentDeviceId).FirstOrDefault();
    }

    private static string GetExplicitCurrentDeviceGamePath(GameDefinition? gameDefinition, string? currentDeviceId)
    {
        if (gameDefinition is null || string.IsNullOrWhiteSpace(currentDeviceId))
        {
            return string.Empty;
        }

        return gameDefinition.GamePaths.TryGetValue(currentDeviceId, out var path)
            ? path
            : string.Empty;
    }

    private static bool IsZipSelectionPath(string path) =>
        path.StartsWith("content://", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedAndroidPath(string path)
    {
#if ANDROID
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.StartsWith("/", StringComparison.Ordinal)
            || path.StartsWith("content://", StringComparison.OrdinalIgnoreCase);
#else
        return Path.IsPathRooted(path);
#endif
    }


}

public sealed partial class GameListItemViewModel : ObservableObject
{
    public GameListItemViewModel(GameSnapshot game, bool isFavorite)
    {
        Game = game;
        Name = game.Name;
        IsFavorite = isFavorite;
        Summary = $"{(game.CloudSyncEnabled ? "Cloud" : "Local")} | {BuildSaveUnitSummary(game)}";
    }

    public GameSnapshot Game { get; }

    public string Name { get; }

    public string Summary { get; }

    [ObservableProperty]
    private bool _isFavorite;

    public string FavoriteButtonText => IsFavorite ? "Unfavorite" : "Favorite";

    partial void OnIsFavoriteChanged(bool value)
    {
        OnPropertyChanged(nameof(FavoriteButtonText));
    }

    private static string BuildSaveUnitSummary(GameSnapshot game)
    {
        var fileCount = game.SaveUnits.Count(unit => unit.UnitType == SaveUnitType.File);
        var folderCount = game.SaveUnits.Count(unit => unit.UnitType == SaveUnitType.Folder);
        var zipCount = game.SaveUnits.Count(unit => unit.UnitType == SaveUnitType.Zip);
        return $"{folderCount} folder, {fileCount} file, {zipCount} ZIP";
    }
}

public sealed record SaveUnitTargetOption(int? SaveUnitId, SaveUnitType UnitType, bool IsNew, string Label)
{
    public static SaveUnitTargetOption Existing(int saveUnitId, SaveUnitType unitType, string label) =>
        new(saveUnitId, unitType, false, label);

    public static SaveUnitTargetOption New(SaveUnitType unitType, string label) =>
        new(null, unitType, true, label);
}
