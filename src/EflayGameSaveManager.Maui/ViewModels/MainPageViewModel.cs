using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services;
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

    [ObservableProperty]
    private SaveUnitTargetOption? _selectedSaveUnitTarget;

    [ObservableProperty]
    private string _editSavePath = string.Empty;

    [ObservableProperty]
    private string _editGamePath = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isAddGameVisible;

    [ObservableProperty]
    private bool _isNavigationDrawerOpen;

    [ObservableProperty]
    private string _selectedNavigationTab = NavigationTabAllGames;

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
        _currentSnapshot = _gameLibraryService.CreateSnapshot(_currentConfig, _configPath, deviceName);

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
            var zipSelection = await _storagePickerService.PickZipFileAsync();
            if (!zipSelection.IsSuccess)
            {
                StatusMessage = zipSelection.Message;
                return;
            }

            await RunBusyAsync(
                $"Uploading {game.Name} zip save...",
                async () =>
                {
                    var result = await _cloudSyncService.UploadGameCurrentSaveAsync(
                        game,
                        snapshot.CurrentDevice,
                        cloudSettings,
                        zipSelection.Path);
                    StatusMessage = $"Uploaded zip save to {result.RootKey}.";
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
            $"Uploading {game.Name} current save...",
            async () =>
            {
#if ANDROID
                var tempDirs = await StageRestrictedPathsForUploadAsync(game.Name, snapshot.CurrentDevice.DeviceId);
#endif
                try
                {
                    // Rebuild snapshot with potentially redirected paths
                    var uploadSnapshot = _currentConfig is not null
                        ? _gameLibraryService.CreateSnapshot(_currentConfig, _configPath, snapshot.CurrentDevice.DeviceName)
                        : snapshot;

                    var result = await _cloudSyncService.UploadGameCurrentSaveAsync(
                        uploadSnapshot.Games.First(g => g.Name == game.Name),
                        snapshot.CurrentDevice, cloudSettings);
                    StatusMessage = $"Uploaded current save to {result.RootKey}.";
                    await RefreshCloudCoreAsync(game, snapshot, cloudSettings);
                }
                finally
                {
#if ANDROID
                    CleanupTempDirs(tempDirs);
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

                        var normalizedArchivePath = _archiveTransferService.NormalizeZipArchiveForMobileRestore(downloadedArchivePath, workRoot);
                        await using var archiveStream = File.OpenRead(normalizedArchivePath);
                        var saveResult = await _storagePickerService.SaveFileAsync($"{game.Name}.zip", archiveStream);
                        StatusMessage = saveResult.IsSuccess
                            ? $"Saved restored zip from {result.RootKey}."
                            : saveResult.Message;
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
#if ANDROID
                var redirectMap = await RedirectRestrictedPathsForRestoreAsync(game.Name, snapshot.CurrentDevice.DeviceId);
#endif
                try
                {
                    var restoreSnapshot = _currentConfig is not null
                        ? _gameLibraryService.CreateSnapshot(_currentConfig, _configPath, snapshot.CurrentDevice.DeviceName)
                        : snapshot;

                    var result = await _cloudSyncService.RestoreGameLatestSaveAsync(
                        restoreSnapshot.Games.First(g => g.Name == game.Name),
                        snapshot.CurrentDevice, cloudSettings);
                    StatusMessage = $"Restored latest cloud save from {result.RootKey}.";
#if ANDROID
                    await ApplyRestoredFilesToRestrictedPathsAsync(redirectMap);
#endif
                    await RefreshCloudCoreAsync(game, snapshot, cloudSettings);
                }
                finally
                {
#if ANDROID
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

    private async Task<List<string>> StageRestrictedPathsForUploadAsync(string gameName, string deviceId)
    {
        var tempDirs = new List<string>();
        var gameDef = _currentConfig?.Games.FirstOrDefault(g =>
            string.Equals(g.Name, gameName, StringComparison.OrdinalIgnoreCase));
        if (gameDef is null || _currentConfig is null) return tempDirs;

        var stagingRoot = Path.Combine(FileSystem.CacheDirectory, "shizuku-upload", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);

        foreach (var unit in gameDef.SavePaths)
        {
            if (!unit.Paths.TryGetValue(deviceId, out var originalPath) ||
                string.IsNullOrWhiteSpace(originalPath) ||
                !IsRestrictedPath(originalPath))
                continue;

            var stagedPath = await Platforms.Android.ShizukuFileService.CopyFromRestrictedAsync(
                originalPath, stagingRoot);
            if (stagedPath is not null)
            {
                tempDirs.Add(stagedPath);
                unit.Paths[deviceId] = stagedPath;
            }
        }

        return tempDirs;
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

            // Replace restricted path with a temp dir for Core's restore
            var tempDir = Path.Combine(stagingRoot, $"unit-{unit.Id}");
            unit.Paths[deviceId] = tempDir;
            redirects.Add(new RestoreRedirect(originalPath, tempDir, deviceId, unit.Id));
        }

        return redirects;
    }

    private static async Task ApplyRestoredFilesToRestrictedPathsAsync(List<RestoreRedirect> redirects)
    {
        foreach (var r in redirects)
        {
            if (Directory.Exists(r.TempDir) || File.Exists(r.TempDir))
            {
                await Platforms.Android.ShizukuFileService.CopyToRestrictedAsync(r.TempDir, r.OriginalPath);
            }
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
        var existingGame = _currentConfig.Games.FirstOrDefault(game =>
            string.Equals(game.Name, gameName, StringComparison.OrdinalIgnoreCase));

        if (!isZipMode && string.IsNullOrWhiteSpace(savePath))
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
                    var folderUnit = existingGame.SavePaths.FirstOrDefault(unit => unit.UnitType == SaveUnitType.Folder);
                    var zipUnit = new SaveUnitDefinition
                    {
                        Id = existingGame.NextSaveUnitId,
                        UnitType = SaveUnitType.Zip,
                        DeleteBeforeApply = false,
                        LinkedUnitIds = folderUnit is null ? [] : [folderUnit.Id]
                    };

                    if (folderUnit is not null && !folderUnit.LinkedUnitIds.Contains(zipUnit.Id))
                    {
                        folderUnit.LinkedUnitIds.Add(zipUnit.Id);
                    }

                    existingGame.SavePaths.Add(zipUnit);
                    existingGame.NextSaveUnitId++;
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
        var selection = await _storagePickerService.PickZipFileAsync();
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
        return SelectedGame?.Game.SaveUnits.Any(unit => unit.UnitType == SaveUnitType.Zip) == true;
    }

    [RelayCommand]
    private async Task PickSaveFileAsync()
    {
        var selection = await _storagePickerService.PickFileAsync();
        if (!selection.IsSuccess)
        {
            StatusMessage = selection.Message;
            return;
        }

        SelectedSaveUnitType = "File";
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
        var selection = await _storagePickerService.PickFileAsync();
        if (!selection.IsSuccess)
        {
            StatusMessage = selection.Message;
            return;
        }

        if (SelectedSaveUnitTarget?.IsNew == true)
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
                SaveUnitDefinition targetUnit;
                if (SelectedSaveUnitTarget.IsNew)
                {
                    var compatibleExistingUnit = game.SavePaths
                        .Where(unit => unit.UnitType == SelectedSaveUnitTarget.UnitType)
                        .OrderBy(unit => unit.Id)
                        .FirstOrDefault();

                    if (compatibleExistingUnit is not null)
                    {
                        targetUnit = compatibleExistingUnit;
                    }
                    else
                    {
                        targetUnit = new SaveUnitDefinition
                        {
                            Id = game.NextSaveUnitId,
                            UnitType = SelectedSaveUnitTarget.UnitType,
                            DeleteBeforeApply = false,
                            Paths = new Dictionary<string, string>()
                        };
                        game.NextSaveUnitId++;
                        game.SavePaths.Add(targetUnit);
                    }
                }
                else
                {
                    targetUnit = game.SavePaths.FirstOrDefault(unit => unit.Id == SelectedSaveUnitTarget.SaveUnitId)
                                 ?? throw new InvalidOperationException($"Save unit not found: {SelectedSaveUnitTarget.SaveUnitId}");
                }

                targetUnit.Paths[deviceId] = savePath;

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

        var selectedGameDefinition = TryGetSelectedGameDefinition(game.Name);
        var deviceId = _currentSnapshot?.CurrentDevice.DeviceId;
        EditGamePath = GetExplicitCurrentDeviceGamePath(selectedGameDefinition, deviceId);

        var currentDevicePath = GetExplicitCurrentDevicePath(selectedGameDefinition, deviceId);
        if (currentDevicePath is not null)
        {
            SelectedSaveUnitTarget = SaveUnitTargets.FirstOrDefault(option =>
                !option.IsNew && option.SaveUnitId == currentDevicePath.SaveUnitId);
            EditSavePath = currentDevicePath.Path;
            return;
        }

        SelectedSaveUnitTarget = SaveUnitTargets.FirstOrDefault(option => !option.IsNew)
                                 ?? SaveUnitTargets.FirstOrDefault();
        EditSavePath = string.Empty;
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
        game = CreateAndroidScopedGameSnapshot(SelectedGame.Game, selectedGameDefinition, _currentSnapshot.CurrentDevice.DeviceId);
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

    private static string BuildCloudDetails(GameCloudStatus status, IReadOnlyList<CloudGameBackup> backups)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Game: {status.GameName}");
        builder.AppendLine($"Cloud root: {status.RootKey}");
        builder.AppendLine($"Cloud data exists: {status.IsAvailable}");
        builder.AppendLine($"Current head: {DisplayValue(status.CurrentHead)}");
        builder.AppendLine($"Backup count: {status.BackupCount}");
        builder.AppendLine();
        builder.AppendLine("Recent backups:");

        if (backups.Count == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (var backup in backups.Take(8))
            {
                builder.Append("- ")
                    .Append(backup.Date)
                    .Append(" | ")
                    .Append(FormatSize(backup.Size))
                    .Append(" | ")
                    .Append(backup.DeviceId);

                if (backup.IsCurrentDeviceHead)
                {
                    builder.Append(" | current");
                }

                builder.AppendLine();
            }
        }

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

    private static GameSnapshot CreateAndroidScopedGameSnapshot(
        GameSnapshot source,
        GameDefinition gameDefinition,
        string currentDeviceId)
    {
        var explicitSaveUnits = source.SaveUnits
            .Where(unit => gameDefinition.SavePaths.Any(definition =>
                definition.Id == unit.Id &&
                definition.Paths.TryGetValue(currentDeviceId, out var configuredPath) &&
                !string.IsNullOrWhiteSpace(configuredPath)))
            .Select(unit =>
            {
                var explicitPath = gameDefinition.SavePaths
                    .Where(definition => definition.Id == unit.Id)
                    .SelectMany(definition => definition.Paths)
                    .Where(path => string.Equals(path.Key, currentDeviceId, StringComparison.OrdinalIgnoreCase))
                    .Select(path => path.Value)
                    .FirstOrDefault() ?? string.Empty;

                return unit with
                {
                    Paths =
                    [
                        new ResolvedDevicePath(
                            currentDeviceId,
                            unit.Paths.FirstOrDefault(path => string.Equals(path.DeviceId, currentDeviceId, StringComparison.OrdinalIgnoreCase))?.DeviceName ?? "Android",
                            explicitPath)
                    ]
                };
            })
            .ToArray();

        var explicitCurrentDevicePaths = explicitSaveUnits
            .Select(unit => new CurrentDevicePathInfo(
                unit.Id,
                unit.UnitType,
                unit.Paths[0].Path,
                unit.DeleteBeforeApply))
            .ToArray();

        var explicitGamePath = gameDefinition.GamePaths.TryGetValue(currentDeviceId, out var gamePath)
            ? gamePath
            : string.Empty;

        return source with
        {
            SaveUnits = explicitSaveUnits,
            CurrentDevicePaths = explicitCurrentDevicePaths,
            CurrentDeviceGamePath = new CurrentDeviceGamePathInfo(explicitGamePath)
        };
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
        return $"{folderCount} folder, {fileCount} file";
    }
}

public sealed record SaveUnitTargetOption(int? SaveUnitId, SaveUnitType UnitType, bool IsNew, string Label)
{
    public static SaveUnitTargetOption Existing(int saveUnitId, SaveUnitType unitType, string label) =>
        new(saveUnitId, unitType, false, label);

    public static SaveUnitTargetOption New(SaveUnitType unitType, string label) =>
        new(null, unitType, true, label);
}
