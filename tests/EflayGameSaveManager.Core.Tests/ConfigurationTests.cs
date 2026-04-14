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
}
