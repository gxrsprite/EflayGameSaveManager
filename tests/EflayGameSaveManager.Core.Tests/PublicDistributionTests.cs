using EflayGameSaveManager.Core.Services;
using EflayGameSaveManager.Core.Services.GamePlugins;

namespace EflayGameSaveManager.Core.Tests;

public sealed class PublicDistributionTests
{
    private static string ProfilePath(string name) => Path.Combine(AppContext.BaseDirectory, "PublicProfile", name);

    [Fact]
    public async Task PublicProfile_ContainsOnlyLambAndTwoGenericDevicesWithoutCloudCredentials()
    {
        var config = await new GameSaveManagerConfigurationService().LoadAsync(ProfilePath("GameSaveManager.config.json"));
        var game = Assert.Single(config.Games);
        Assert.Equal("Cult of the Lamb", game.Name);
        Assert.Equal("咩咩启示录", game.ChineseName);
        Assert.False(game.CloudSyncEnabled);
        Assert.Equal(new[] { "android", "pc" }, config.Devices.Keys.Order().ToArray());
        Assert.Equal("PC", config.Devices["pc"].Name);
        Assert.Equal("Android", config.Devices["android"].Name);
        Assert.Equal("windows", config.Devices["pc"].Platform);
        Assert.Equal("android", config.Devices["android"].Platform);
        foreach (var pair in config.Devices)
        {
            Assert.Equal(pair.Key, pair.Value.Id);
            Assert.False(Guid.TryParse(pair.Value.Id, out _));
        }

        var cloud = config.Settings.CloudSettings;
        Assert.False(cloud.AlwaysSync);
        Assert.Empty(cloud.Backend.Endpoint);
        Assert.Empty(cloud.Backend.Bucket);
        Assert.Empty(cloud.Backend.AccessKeyId);
        Assert.Empty(cloud.Backend.SecretAccessKey);
        Assert.Empty(game.GamePaths);
        Assert.Empty(game.ExternalLaunchers);
    }

    [Theory]
    [InlineData("Windows", "pc", "PC", "windows")]
    [InlineData("Android", "android", "Android", "android")]
    public async Task PublicRuntime_SelectsExistingDeviceWithoutRegisteringHardware(string runtime, string id, string name, string platform)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "CotlPublicProfileTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var configPath = Path.Combine(tempRoot, "GameSaveManager.config.json");
            File.Copy(ProfilePath("GameSaveManager.config.json"), configPath);
            File.Copy(ProfilePath($"{runtime}.runtime.json"), Path.Combine(tempRoot, "GameSaveManager.runtime.json"));
            var config = await new GameSaveManagerConfigurationService().LoadAsync(configPath);
            var snapshot = new GameLibraryService(new EnvironmentTokenResolver(), new CurrentDeviceService())
                .CreateSnapshot(config, configPath, "Unrelated hardware name", platform);

            Assert.Equal(id, snapshot.CurrentDevice.DeviceId);
            Assert.Equal(name, snapshot.CurrentDevice.DeviceName);
            Assert.Equal(2, config.Devices.Count);
            Assert.True(new CultOfTheLambSavePlugin().Matches(Assert.Single(snapshot.Games)));
        }
        finally { Directory.Delete(tempRoot, recursive: true); }
    }

    [Fact]
    public async Task PublicSaveUnit_UsesContentsOnlyAndPreservesDeviceSettings()
    {
        var config = await new GameSaveManagerConfigurationService().LoadAsync(ProfilePath("GameSaveManager.config.json"));
        var unit = Assert.Single(Assert.Single(config.Games).SavePaths);
        Assert.Equal(0, unit.Id);
        Assert.Equal(new[] { "android", "pc" }, unit.Paths.Keys.Order().ToArray());
        Assert.True(unit.ContentsOnly);
        Assert.False(unit.DeleteBeforeApply);
        Assert.Equal(new[] { "settings*", "Prefs.dict" }, unit.Exclude);
        Assert.StartsWith("<winLocalAppDataLow>", unit.Paths["pc"]);
        Assert.Equal("/storage/emulated/0/Android/data/com.cotl.port/files/user", unit.Paths["android"]);
    }
}
