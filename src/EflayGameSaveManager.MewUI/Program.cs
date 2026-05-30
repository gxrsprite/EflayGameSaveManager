using System.Diagnostics;
using System.Text;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services;

namespace EflayGameSaveManager.MewUI;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try
        {
            AppLogger.Info("MewUI Program.Main entered.");
            var app = new SaveManagerMewApplication();
            Application.Create()
                .UseWin32()
                .UseDirect2D()
                .BuildMainWindow(app.BuildWindow)
                .Run();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Fatal startup exception in MewUI.", ex);
            throw;
        }
    }
}

internal sealed class SaveManagerMewApplication
{
    private readonly GameSaveManagerConfigurationService _configurationService = new();
    private readonly GameLibraryService _gameLibraryService = new(
        new EnvironmentTokenResolver(),
        new CurrentDeviceService(),
        new AppRuntimeSettingsService());
    private readonly CloudSyncService _cloudSyncService = new(
        new S3CompatibleCloudStorageClient(),
        new SaveBackupService(),
        new ArchiveTransferService());

    private Window? _window;
    private ListBox? _gameList;
    private Label? _configPathLabel;
    private Label? _backupRootLabel;
    private Label? _deviceLabel;
    private Label? _selectedGameLabel;
    private Label? _statusLabel;
    private MultiLineTextBox? _detailsBox;
    private MultiLineTextBox? _cloudBox;
    private TextBox? _addGameNameBox;
    private TextBox? _addSavePathBox;
    private TextBox? _addGamePathBox;

    private ManagerConfig? _currentConfig;
    private AppSnapshot? _currentSnapshot;
    private GameSnapshot? _selectedGame;
    private bool _isBusy;

    public Window BuildWindow()
    {
        _configPathLabel = new Label().Text("Config: loading...");
        _backupRootLabel = new Label().Text("Backup root: loading...");
        _deviceLabel = new Label().Text("Device: loading...");
        _selectedGameLabel = new Label().Text("Selected game: none").Bold();
        _statusLabel = new Label().Text("Starting...");
        _detailsBox = new MultiLineTextBox().IsReadOnly(true).Wrap(true).Height(240);
        _cloudBox = new MultiLineTextBox().IsReadOnly(true).Wrap(true).Height(220);
        _addGameNameBox = new TextBox().Margin(0, 0, 0, 6);
        _addSavePathBox = new TextBox().Margin(0, 0, 0, 6);
        _addGamePathBox = new TextBox().Margin(0, 0, 0, 6);

        _gameList = new ListBox()
            .ItemHeight(30)
            .OnSelectionChanged(_ => HandleGameSelectionChanged());

        var leftPane = new StackPanel()
            .Spacing(10)
            .Padding(12)
            .Children(
                new Label().Text("Games").Bold(),
                new Label().Text("Current configuration game list."),
                _gameList,
                new Button().Content("Reload").OnClick(async () => await ReloadAsync()),
                new Button().Content("Refresh cloud").OnClick(async () => await RefreshCloudAsync()));

        var actionRow1 = new StackPanel()
            .Horizontal()
            .Spacing(8)
            .Children(
                new Button().Content("Open save").OnClick(OpenSelectedSavePath),
                new Button().Content("Run game").OnClick(RunSelectedGame));

        var actionRow2 = new StackPanel()
            .Horizontal()
            .Spacing(8)
            .Children(
                new Button().Content("Open game folder").OnClick(OpenSelectedGameFolder),
                new Button().Content("Upload current").OnClick(async () => await UploadCurrentSaveAsync()),
                new Button().Content("Restore current").OnClick(async () => await RestoreCurrentSaveAsync()));

        var rightPane = new StackPanel()
            .Spacing(10)
            .Padding(12)
            .Children(
                new Label().Text("Overview").Bold(),
                _configPathLabel,
                _backupRootLabel,
                _deviceLabel,
                _selectedGameLabel,
                _statusLabel,
                actionRow1,
                actionRow2,
                new Label().Text("Game details").Bold(),
                _detailsBox,
                new Label().Text("Cloud details").Bold(),
                _cloudBox,
                new Label().Text("Quick add game").Bold(),
                new Label().Text("Game name"),
                _addGameNameBox,
                new Label().Text("Current device save path"),
                _addSavePathBox,
                new Label().Text("Game executable path (optional)"),
                _addGamePathBox,
                new Button().Content("Add folder-save game").OnClick(async () => await AddGameAsync()));

        _window = new Window()
            .Title("Eflay Game Save Manager - MewUI")
            .Resizable(1280, 860, 960, 640, 1800, 1400)
            .Content(
                new SplitPanel()
                    .Horizontal()
                    .FirstLength(GridLength.Pixels(300))
                    .MinFirst(240)
                    .MinSecond(500)
                    .First(leftPane)
                    .Second(rightPane));

        _ = ReloadAsync();
        return _window;
    }

    private async Task ReloadAsync(string? preferredGameName = null)
    {
        await RunBusyAsync(
            "Loading configuration...",
            async () =>
            {
                var configPath = _configurationService.GetDefaultConfigPath();
                var configWasCreatedInMemory = false;
                ManagerConfig config;

                try
                {
                    configPath = _configurationService.FindConfigPath();
                    config = await _configurationService.LoadAsync(configPath);
                }
                catch (FileNotFoundException)
                {
                    config = _configurationService.CreateDefault();
                    configWasCreatedInMemory = true;
                }

                var snapshot = _gameLibraryService.CreateSnapshot(config, configPath);
                _currentConfig = config;
                _currentSnapshot = snapshot;

                _configPathLabel!.Text = $"Config: {configPath}";
                _backupRootLabel!.Text = $"Backup root: {snapshot.BackupRoot}";
                _deviceLabel!.Text = $"Device: {snapshot.CurrentDevice.DeviceName} [{snapshot.CurrentDevice.DeviceId}]";

                var gameList = _gameList!;
                var gameNames = snapshot.Games.Select(game => new GameListEntry(game.Name)).ToList();
                gameList
                    .Items(gameNames, item => item.Name, item => item.Name)
                    .ItemPadding(8);

                if (gameNames.Count == 0)
                {
                    gameList.SelectedIndex = -1;
                    SetSelectedGame(null);
                }
                else
                {
                    var targetName = string.IsNullOrWhiteSpace(preferredGameName)
                        ? _selectedGame?.Name
                        : preferredGameName;
                    var selectedIndex = gameNames.FindIndex(item => string.Equals(item.Name, targetName, StringComparison.Ordinal));
                    if (selectedIndex < 0)
                    {
                        selectedIndex = 0;
                    }

                    gameList.SelectedIndex = selectedIndex;
                    SetSelectedGame(snapshot.Games[selectedIndex]);
                }

                SetStatus(
                    configWasCreatedInMemory
                        ? $"No config found. A new {GameSaveManagerConfigurationService.ConfigFileName} will be created when you add a game."
                        : $"Loaded {snapshot.Games.Count} games for {snapshot.CurrentDevice.DeviceName}.");
            });
    }

    private void HandleGameSelectionChanged()
    {
        if (_currentSnapshot is null || _gameList is null)
        {
            return;
        }

        var selectedIndex = _gameList.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= _currentSnapshot.Games.Count)
        {
            SetSelectedGame(null);
            return;
        }

        SetSelectedGame(_currentSnapshot.Games[selectedIndex]);
    }

    private void SetSelectedGame(GameSnapshot? game)
    {
        _selectedGame = game;
        _selectedGameLabel!.Text = game is null ? "Selected game: none" : $"Selected game: {game.Name}";
        _detailsBox!.Text = game is null ? "No game selected." : BuildGameDetails(game);
        _cloudBox!.Text = game is null ? "No game selected." : "Cloud status not loaded. Click Refresh cloud.";
    }

    private async Task RefreshCloudAsync()
    {
        if (_selectedGame is null || _currentSnapshot is null || _currentConfig is null)
        {
            SetStatus("Select a game first.");
            return;
        }

        if (!TryGetCloudSettings(out var cloudSettings))
        {
            _cloudBox!.Text = "Cloud backend configuration is incomplete.";
            return;
        }

        await RunBusyAsync(
            $"Refreshing cloud status for {_selectedGame.Name}...",
            async () =>
            {
                var status = await _cloudSyncService.GetGameCurrentStatusAsync(_selectedGame, _currentSnapshot.CurrentDevice, cloudSettings);
                var backups = await _cloudSyncService.ListGameBackupsAsync(_selectedGame, _currentSnapshot.CurrentDevice, cloudSettings);
                _cloudBox!.Text = BuildCloudDetails(status, backups);
                SetStatus($"Cloud status refreshed for {_selectedGame.Name}.");
            });
    }

    private async Task UploadCurrentSaveAsync()
    {
        if (_selectedGame is null || _currentSnapshot is null)
        {
            SetStatus("Select a game first.");
            return;
        }

        if (!TryGetCloudSettings(out var cloudSettings))
        {
            _cloudBox!.Text = "Cloud backend configuration is incomplete.";
            return;
        }

        await RunBusyAsync(
            $"Uploading {_selectedGame.Name} current save...",
            async () =>
            {
                var result = await _cloudSyncService.UploadGameCurrentSaveAsync(_selectedGame, _currentSnapshot.CurrentDevice, cloudSettings);
                SetStatus($"Uploaded current save to {result.RootKey}");
                await RefreshCloudAsync();
            });
    }

    private async Task RestoreCurrentSaveAsync()
    {
        if (_selectedGame is null || _currentSnapshot is null)
        {
            SetStatus("Select a game first.");
            return;
        }

        if (!TryGetCloudSettings(out var cloudSettings))
        {
            _cloudBox!.Text = "Cloud backend configuration is incomplete.";
            return;
        }

        await RunBusyAsync(
            $"Restoring {_selectedGame.Name} current cloud save...",
            async () =>
            {
                var result = await _cloudSyncService.RestoreGameCurrentSaveAsync(_selectedGame, _currentSnapshot.CurrentDevice, cloudSettings);
                SetStatus($"Restored current cloud save from {result.RootKey}");
                await RefreshCloudAsync();
            });
    }

    private async Task AddGameAsync()
    {
        if (_currentConfig is null || _currentSnapshot is null)
        {
            SetStatus("Configuration is not loaded yet.");
            return;
        }

        var gameName = _addGameNameBox!.Text?.Trim() ?? string.Empty;
        var savePath = _addSavePathBox!.Text?.Trim() ?? string.Empty;
        var gamePath = _addGamePathBox!.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(gameName))
        {
            SetStatus("Game name is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(savePath))
        {
            SetStatus("Current device save path is required.");
            return;
        }

        if (_currentConfig.Games.Any(game => string.Equals(game.Name, gameName, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus($"Game already exists: {gameName}");
            return;
        }

        await RunBusyAsync(
            $"Adding game {gameName}...",
            async () =>
            {
                var newGame = new GameDefinition
                {
                    Name = gameName,
                    SavePaths =
                    [
                        new SaveUnitDefinition
                        {
                            Id = 0,
                            UnitType = SaveUnitType.Folder,
                            DeleteBeforeApply = false,
                            Paths = new Dictionary<string, string>
                            {
                                [_currentSnapshot.CurrentDevice.DeviceId] = savePath
                            }
                        }
                    ],
                    GamePaths = string.IsNullOrWhiteSpace(gamePath)
                        ? []
                        : new Dictionary<string, string>
                        {
                            [_currentSnapshot.CurrentDevice.DeviceId] = gamePath
                        },
                    NextSaveUnitId = 1,
                    CloudSyncEnabled = true
                };

                _currentConfig.Games.Add(newGame);
                await _configurationService.SaveAsync(_currentSnapshot.ConfigPath, _currentConfig);
                _addGameNameBox.Text = string.Empty;
                _addSavePathBox.Text = string.Empty;
                _addGamePathBox.Text = string.Empty;
                await ReloadAsync(gameName);
                SetStatus($"Added game: {gameName}");
            });
    }

    private async Task RunBusyAsync(string statusMessage, Func<Task> action)
    {
        if (_isBusy)
        {
            SetStatus("Another operation is still running.");
            return;
        }

        _isBusy = true;
        SetStatus(statusMessage);

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            AppLogger.Error(statusMessage, ex);
            SetStatus(ex.Message);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private bool TryGetCloudSettings(out CloudSettings cloudSettings)
    {
        cloudSettings = _currentConfig?.Settings.CloudSettings ?? new CloudSettings();
        return string.Equals(cloudSettings.Backend.Type, "S3", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(cloudSettings.Backend.Endpoint) &&
               !string.IsNullOrWhiteSpace(cloudSettings.Backend.Bucket) &&
               !string.IsNullOrWhiteSpace(cloudSettings.Backend.AccessKeyId) &&
               !string.IsNullOrWhiteSpace(cloudSettings.Backend.SecretAccessKey);
    }

    private void OpenSelectedSavePath()
    {
        if (_selectedGame is null)
        {
            SetStatus("Select a game first.");
            return;
        }

        var savePath = _selectedGame.CurrentDevicePaths.FirstOrDefault()?.Path;
        if (string.IsNullOrWhiteSpace(savePath))
        {
            SetStatus("Current device save path is empty.");
            return;
        }

        if (savePath.StartsWith("HKEY_", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Registry save units cannot be opened in Explorer.");
            return;
        }

        if (File.Exists(savePath))
        {
            var directory = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                OpenPath(directory);
                return;
            }
        }

        OpenPath(savePath);
    }

    private void OpenSelectedGameFolder()
    {
        if (_selectedGame is null)
        {
            SetStatus("Select a game first.");
            return;
        }

        var gamePath = _selectedGame.CurrentDeviceGamePath.Path;
        if (string.IsNullOrWhiteSpace(gamePath))
        {
            SetStatus("Current device game path is empty.");
            return;
        }

        var directory = File.Exists(gamePath) ? Path.GetDirectoryName(gamePath) : gamePath;
        if (string.IsNullOrWhiteSpace(directory))
        {
            SetStatus("Cannot resolve the game folder.");
            return;
        }

        OpenPath(directory);
    }

    private void RunSelectedGame()
    {
        if (_selectedGame is null)
        {
            SetStatus("Select a game first.");
            return;
        }

        var gamePath = _selectedGame.CurrentDeviceGamePath.Path;
        if (string.IsNullOrWhiteSpace(gamePath))
        {
            SetStatus("Current device game path is empty.");
            return;
        }

        if (!File.Exists(gamePath))
        {
            SetStatus($"Game executable not found: {gamePath}");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = gamePath,
            WorkingDirectory = Path.GetDirectoryName(gamePath) ?? AppContext.BaseDirectory,
            UseShellExecute = true
        });

        SetStatus($"Started {Path.GetFileName(gamePath)}");
    }

    private void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("Path is empty.");
            return;
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            SetStatus($"Path not found: {path}");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private void SetStatus(string message)
    {
        _statusLabel!.Text = $"Status: {message}";
    }

    private static string BuildGameDetails(GameSnapshot game)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Game: {game.Name}");
        builder.AppendLine($"Cloud sync enabled: {game.CloudSyncEnabled}");
        builder.AppendLine($"Current device game path: {DisplayValue(game.CurrentDeviceGamePath.Path)}");
        builder.AppendLine();
        builder.AppendLine("Current device save paths:");

        if (game.CurrentDevicePaths.Count == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (var path in game.CurrentDevicePaths)
            {
                builder.AppendLine($"- unit {path.SaveUnitId} [{path.UnitType}] {DisplayValue(path.Path)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("All configured save units:");
        foreach (var unit in game.SaveUnits)
        {
            builder.AppendLine($"- unit {unit.Id} [{unit.UnitType}] delete-before-apply={unit.DeleteBeforeApply}");
            foreach (var path in unit.Paths)
            {
                builder.AppendLine($"  {path.DeviceName}: {DisplayValue(path.Path)}");
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
            foreach (var backup in backups.Take(12))
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

                if (backup.IsDeviceHead && !backup.IsCurrentDeviceHead)
                {
                    builder.Append(" | device-head");
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

    private sealed record GameListEntry(string Name);
}
