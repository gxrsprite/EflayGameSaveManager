using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services;

namespace EflayGameSaveManager.Core.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public async Task LoadAsync_DeserializesRootConfiguration()
    {
        var service = new GameSaveManagerConfigurationService();
        var configPath = Path.Combine(AppContext.BaseDirectory, "GameSaveManager.config.json");

        var config = await service.LoadAsync(configPath);

        Assert.NotNull(config);
        Assert.NotEmpty(config.Games);
        Assert.NotEmpty(config.Devices);
        Assert.NotEqual(System.Text.Json.JsonValueKind.Undefined, config.QuickAction.QuickActionGame.ValueKind);
    }

    [Theory]
    [InlineData("<winDocuments>\\MyGame", Environment.SpecialFolder.MyDocuments)]
    [InlineData("<home>\\Saved Games", Environment.SpecialFolder.UserProfile)]
    public void ResolvePath_ExpandsKnownTokens(string input, Environment.SpecialFolder specialFolder)
    {
        var resolver = new EnvironmentTokenResolver();

        var resolved = resolver.ResolvePath(input);

        Assert.StartsWith(Environment.GetFolderPath(specialFolder), resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateSnapshot_ResolvesRelativeBackupRoot()
    {
        var service = new GameLibraryService(
            new EnvironmentTokenResolver(),
            new CurrentDeviceService(),
            new AppRuntimeSettingsService());
        var config = new ManagerConfig
        {
            BackupPath = "./save_data",
            Games = [],
            Devices = []
        };

        var snapshot = service.CreateSnapshot(config, @"G:\Projects\my-lab\Tools\EflayGameSaveManager\EflayGameSaveManager\GameSaveManager.config.json");

        Assert.EndsWith(Path.Combine("EflayGameSaveManager", "save_data"), snapshot.BackupRoot);
    }

    [Fact]
    public async Task SaveAsync_WritesDefaultConfigurationWhenFileDoesNotExist()
    {
        var root = Path.Combine(Path.GetTempPath(), "EflayGameSaveManager.Tests", Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, GameSaveManagerConfigurationService.ConfigFileName);
        var service = new GameSaveManagerConfigurationService();
        var config = service.CreateDefault();

        try
        {
            await service.SaveAsync(configPath, config);
            var loaded = await service.LoadAsync(configPath);

            Assert.True(File.Exists(configPath));
            Assert.Equal("1.0.0", loaded.Version);
            Assert.Empty(loaded.Games);
            Assert.Equal("./save_data", loaded.BackupPath);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
