using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services;
using LVGLSharp;
using LVGLSharp.Drawing;
using LVGLSharp.Forms;
using LVGLSharp.Interop;

namespace EflayGameSaveManager.Lvgl;

public sealed unsafe class MainForm : Form
{
    private const int PageSize = 24;
    private const float UiFontSize = 14f;
    private static readonly string? SystemHeitiFontPath = ResolveSystemHeitiFontPath();
    private readonly Label _summaryLabel;
    private readonly Label _cloudStatusLabel;
    private readonly Panel _gamesPanel;
    private readonly TextBox _detailsTextBox;
    private readonly Button _reloadButton;
    private readonly Button _syncButton;
    private readonly Button _restoreButton;
    private readonly Button _prevPageButton;
    private readonly Button _nextPageButton;
    private readonly Label _pageLabel;
    private IReadOnlyList<EflayGameSaveManager.Core.Models.GameSnapshot> _games = [];
    private int _pageIndex;
    private string? _selectedGameName;

    private readonly GameSaveManagerConfigurationService _configurationService = new();
    private readonly GameLibraryService _libraryService = new(
        new EnvironmentTokenResolver(),
        new CurrentDeviceService(),
        new AppRuntimeSettingsService());
    private readonly CloudSyncService _cloudSyncService = new(
        new S3CompatibleCloudStorageClient(),
        new SaveBackupService(),
        new ArchiveTransferService());
    private ManagerConfig? _currentConfig;
    private AppSnapshot? _currentSnapshot;
    private SixLaborsFontManager? _managedFontManager;

    public MainForm()
    {
        Text = "Eflay Game Save Manager - LVGLSharp";
        ClientSize = new Size(1200, 760);

        _summaryLabel = new Label
        {
            Left = 16,
            Top = 16,
            Width = 900,
            Height = 48
        };

        _cloudStatusLabel = new Label
        {
            Left = 380,
            Top = 52,
            Width = 760,
            Height = 24
        };

        _reloadButton = new Button
        {
            Text = "Reload",
            Left = 980,
            Top = 12,
            Width = 160,
            Height = 36
        };
        _reloadButton.Click += (_, _) => ReloadSnapshot();

        _syncButton = new Button
        {
            Text = "Sync Selected",
            Left = 804,
            Top = 12,
            Width = 160,
            Height = 36
        };
        _syncButton.Click += (_, _) => SyncSelectedGame();

        _restoreButton = new Button
        {
            Text = "Restore Selected",
            Left = 628,
            Top = 12,
            Width = 160,
            Height = 36
        };
        _restoreButton.Click += (_, _) => RestoreSelectedGame();

        _prevPageButton = new Button
        {
            Text = "Prev",
            Left = 16,
            Top = 708,
            Width = 80,
            Height = 32
        };
        _prevPageButton.Click += (_, _) =>
        {
            if (_pageIndex > 0)
            {
                _pageIndex--;
                RenderGameButtons(_games);
            }
        };

        _pageLabel = new Label
        {
            Left = 108,
            Top = 712,
            Width = 180,
            Height = 24
        };

        _nextPageButton = new Button
        {
            Text = "Next",
            Left = 290,
            Top = 708,
            Width = 80,
            Height = 32
        };
        _nextPageButton.Click += (_, _) =>
        {
            if ((_pageIndex + 1) * PageSize < _games.Count)
            {
                _pageIndex++;
                RenderGameButtons(_games);
            }
        };

        _gamesPanel = new Panel
        {
            Left = 16,
            Top = 80,
            Width = 340,
            Height = 620
        };

        _detailsTextBox = new TextBox
        {
            Left = 380,
            Top = 80,
            Width = 760,
            Height = 620,
            Multiline = true,
            ReadOnly = true
        };

        Controls.Add(_summaryLabel);
        Controls.Add(_cloudStatusLabel);
        Controls.Add(_syncButton);
        Controls.Add(_restoreButton);
        Controls.Add(_reloadButton);
        Controls.Add(_prevPageButton);
        Controls.Add(_pageLabel);
        Controls.Add(_nextPageButton);
        Controls.Add(_gamesPanel);
        Controls.Add(_detailsTextBox);

        Load += (_, _) => ReloadSnapshot();
    }

    private void ReloadSnapshot()
    {
        try
        {
            var configPath = _configurationService.FindConfigPath();
            var config = _configurationService.LoadAsync(configPath).GetAwaiter().GetResult();
            var snapshot = _libraryService.CreateSnapshot(config, configPath);
            _currentConfig = config;
            _currentSnapshot = snapshot;
            _games = snapshot.Games;
            _pageIndex = 0;
            _selectedGameName = snapshot.Games.FirstOrDefault()?.Name;

            RenderGameButtons(snapshot.Games);

            _summaryLabel.Text = $"Config v{config.Version} | Games: {snapshot.Games.Count} | Device: {snapshot.CurrentDevice.DeviceName}";
            _cloudStatusLabel.Text = GetCloudSummary(config);
            _detailsTextBox.Text = $"Config: {snapshot.ConfigPath}{Environment.NewLine}{Environment.NewLine}Select a game to inspect save paths.";
            if (snapshot.Games.Count > 0)
            {
                ShowGame(snapshot.Games[0].Name);
            }
        }
        catch (Exception ex)
        {
            _summaryLabel.Text = "Failed to load configuration.";
            _detailsTextBox.Text = ex.ToString();
        }
    }

    private void RenderGameButtons(IReadOnlyList<EflayGameSaveManager.Core.Models.GameSnapshot> games)
    {
        _gamesPanel.Controls.Clear();

        var pageGames = games.Skip(_pageIndex * PageSize).Take(PageSize).ToArray();
        for (var index = 0; index < pageGames.Length; index++)
        {
            var game = pageGames[index];
            var button = new Button
            {
                Left = 8,
                Top = 8 + (index * 42),
                Width = 320,
                Height = 34,
                Text = game.Name,
                Tag = game.Name
            };
            button.Click += (_, _) =>
            {
                _selectedGameName = (string)button.Tag!;
                ShowGame(_selectedGameName);
            };
            _gamesPanel.Controls.Add(button);
        }

        _prevPageButton.Enabled = _pageIndex > 0;
        _nextPageButton.Enabled = (_pageIndex + 1) * PageSize < games.Count;
        var pageCount = Math.Max(1, (int)Math.Ceiling(games.Count / (double)PageSize));
        _pageLabel.Text = $"Page {_pageIndex + 1}/{pageCount}";
    }

    private void ShowGame(string selectedName)
    {
        var game = _games.FirstOrDefault(item => item.Name == selectedName);
        if (game is null)
        {
            return;
        }

        var lines = new List<string>
        {
            $"Name: {game.Name}",
            $"Cloud sync: {game.CloudSyncEnabled}",
            $"Save units: {game.SaveUnits.Count}",
            string.Empty,
            "Save paths:"
        };

        foreach (var unit in game.SaveUnits)
        {
            lines.Add($"  Unit {unit.Id} ({unit.UnitType})");
            foreach (var path in unit.Paths)
            {
                lines.Add($"    [{path.DeviceName}] {path.Path}");
            }
        }

        lines.Add(string.Empty);
        lines.Add("Executables:");

        if (game.GamePaths.Count == 0)
        {
            lines.Add("  No executable path configured.");
        }
        else
        {
            foreach (var path in game.GamePaths)
            {
                lines.Add($"  [{path.DeviceName}] {path.Path}");
            }
        }

        _detailsTextBox.Text = string.Join(Environment.NewLine, lines);
    }

    private void SyncSelectedGame()
    {
        if (!TryGetSelectedCloudContext(out var game, out var cloudSettings))
        {
            return;
        }

        try
        {
            var result = _cloudSyncService
                .UploadGameCurrentSaveAsync(game, _currentSnapshot!.CurrentDevice, cloudSettings)
                .GetAwaiter()
                .GetResult();
            _cloudStatusLabel.Text = $"Synced: {game.Name} -> {result.RootKey}";
        }
        catch (Exception ex)
        {
            AppLogger.Error($"LVGL sync failed for {_selectedGameName}.", ex);
            _cloudStatusLabel.Text = $"Sync failed: {ex.Message}";
        }
    }

    private void RestoreSelectedGame()
    {
        if (!TryGetSelectedCloudContext(out var game, out var cloudSettings))
        {
            return;
        }

        try
        {
            var result = _cloudSyncService
                .RestoreGameCurrentSaveAsync(game, _currentSnapshot!.CurrentDevice, cloudSettings)
                .GetAwaiter()
                .GetResult();
            _cloudStatusLabel.Text = $"Restored: {game.Name} <- {result.RootKey}";
        }
        catch (Exception ex)
        {
            AppLogger.Error($"LVGL restore failed for {_selectedGameName}.", ex);
            _cloudStatusLabel.Text = $"Restore failed: {ex.Message}";
        }
    }

    private bool TryGetSelectedCloudContext(
        out EflayGameSaveManager.Core.Models.GameSnapshot game,
        out CloudSettings cloudSettings)
    {
        game = default!;
        cloudSettings = default!;

        if (_currentConfig is null || _currentSnapshot is null || string.IsNullOrWhiteSpace(_selectedGameName))
        {
            _cloudStatusLabel.Text = "No game selected.";
            return false;
        }

        game = _currentSnapshot.Games.FirstOrDefault(item => string.Equals(item.Name, _selectedGameName, StringComparison.Ordinal))
               ?? throw new InvalidOperationException($"Selected game not found: {_selectedGameName}");
        cloudSettings = _currentConfig.Settings.CloudSettings;

        if (!game.CloudSyncEnabled)
        {
            _cloudStatusLabel.Text = $"Cloud sync disabled: {game.Name}";
            return false;
        }

        if (!string.Equals(cloudSettings.Backend.Type, "S3", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(cloudSettings.Backend.Endpoint) ||
            string.IsNullOrWhiteSpace(cloudSettings.Backend.Bucket) ||
            string.IsNullOrWhiteSpace(cloudSettings.Backend.AccessKeyId) ||
            string.IsNullOrWhiteSpace(cloudSettings.Backend.SecretAccessKey))
        {
            _cloudStatusLabel.Text = "Cloud backend not configured.";
            return false;
        }

        return true;
    }

    private static string GetCloudSummary(ManagerConfig config)
    {
        var cloud = config.Settings.CloudSettings;
        if (!string.Equals(cloud.Backend.Type, "S3", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(cloud.Backend.Endpoint) ||
            string.IsNullOrWhiteSpace(cloud.Backend.Bucket))
        {
            return "Cloud backend not configured.";
        }

        return $"Cloud: {cloud.Backend.Bucket}/{CloudStoragePathHelper.NormalizeRootPath(cloud.RootPath)}";
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplySystemHeitiManagedFont();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _managedFontManager?.Dispose();
        _managedFontManager = null;
        LvglRuntimeFontRegistry.ClearActiveTextFont();
        base.OnHandleDestroyed(e);
    }

    private void ApplySystemHeitiManagedFont()
    {
        if (Handle == 0 || string.IsNullOrWhiteSpace(SystemHeitiFontPath))
        {
            return;
        }

        var root = (lv_obj_t*)Handle;
        var fallback = LvglFontHelper.GetEffectiveTextFont(root, lv_part_t.LV_PART_MAIN);
        _managedFontManager?.Dispose();
        _managedFontManager = LvglManagedFontHelper.TryApplyManagedFont(
            root,
            SystemHeitiFontPath,
            UiFontSize,
            DeviceDpi,
            fallback,
            out _,
            out _,
            enabled: true);
    }

    private static string? ResolveSystemHeitiFontPath()
    {
        var fontsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var simHeiFontPathCandidates = new[]
        {
            Path.Combine(fontsDirectory, "simhei.ttf"),
            Path.Combine(windowsDirectory, "Fonts", "simhei.ttf")
        };

        return simHeiFontPathCandidates.FirstOrDefault(File.Exists);
    }
}
