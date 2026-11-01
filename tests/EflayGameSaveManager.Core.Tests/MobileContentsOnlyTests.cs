using System.IO.Compression;
using System.Text;
using System.Text.Json;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services;
using EflayGameSaveManager.Core.Services.GamePlugins;
using EflayGameSaveManager.Maui.Services;

namespace EflayGameSaveManager.Core.Tests;

public sealed class MobileContentsOnlyTests
{
    [Fact]
    public void Normalize_MergingDevicePathsPreservesContentModeFiltersAndStableIds()
    {
        var config = Config("Game", Unit(7, "pc", true), Unit(9, "android", true));
        config.Games[0].NextSaveUnitId = 10;

        Assert.True(MobileConfigNormalizer.Normalize(config));

        var unit = Assert.Single(config.Games[0].SavePaths);
        Assert.True(unit.ContentsOnly);
        Assert.Equal(new[] { "settings*", "Prefs.dict" }, unit.Exclude);
        Assert.Equal(7, unit.Id);
        Assert.Equal(10, config.Games[0].NextSaveUnitId);
        Assert.Equal(new[] { "android", "pc" }, unit.Paths.Keys.Order());
        Assert.False(MobileConfigNormalizer.Normalize(config));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Normalize_DoesNotMergeUnitsWithDifferentContentModesOrFilters(bool differInFilters)
    {
        var pc = Unit(0, "pc", true);
        var android = Unit(1, "android", differInFilters);
        if (differInFilters) android.Exclude = ["different-settings*"];
        var config = Config("OtherGame", pc, android);

        Assert.False(MobileConfigNormalizer.Normalize(config));
        Assert.Equal(2, config.Games[0].SavePaths.Count);
    }

    [Theory]
    [InlineData("Cult of the Lamb")]
    [InlineData("咩咩启示录")]
    public void Normalize_RepairsLambContentModeLostByOldMobileClone(string name)
    {
        var config = Config(name, Unit(0, "android", false));
        config.Games[0].SavePaths[0].Exclude = [];

        Assert.True(MobileConfigNormalizer.Normalize(config));
        Assert.True(config.Games[0].SavePaths[0].ContentsOnly);
        Assert.Equal(new[] { "settings*", "Prefs.dict" }, config.Games[0].SavePaths[0].Exclude);
        Assert.False(MobileConfigNormalizer.Normalize(config));
    }

    [Fact]
    public void Normalize_LeavesLinkedUnitIdentitiesAndReferencesIntact()
    {
        var config = Config("OtherGame", Unit(7, "pc", true), Unit(9, "android", true));
        config.Games[0].SavePaths[0].LinkedUnitIds = [9];

        Assert.False(MobileConfigNormalizer.Normalize(config));
        Assert.Equal(new[] { 7, 9 }, config.Games[0].SavePaths.Select(unit => unit.Id));
        Assert.Equal(new[] { 9 }, config.Games[0].SavePaths[0].LinkedUnitIds);
    }

    [Fact]
    public void AndroidSnapshot_RestrictsPathsWithoutDroppingContentModeOrFilters()
    {
        var config = Config("OtherGame", Unit(7, "android", true), Unit(9, "pc", true));
        var source = new GameLibraryService(new EnvironmentTokenResolver(), new CurrentDeviceService())
            .CreateSnapshot(config, Path.Combine(Path.GetTempPath(), "nonexistent-public-config.json"), "Android", "android").Games[0];

        var scoped = AndroidGameSnapshotFactory.Create(source, config.Games[0], "android");

        var unit = Assert.Single(scoped.SaveUnits);
        Assert.True(unit.ContentsOnly);
        Assert.Equal(new[] { "settings*", "Prefs.dict" }, unit.Exclude!);
        Assert.Equal(7, unit.Id);
        Assert.Equal("android", Assert.Single(unit.Paths).DeviceId);
    }

    [Fact]
    public async Task Lamb_ContentModeSurvivesMobileNormalizationStagingAndBothTransferDirections()
    {
        var root = Path.Combine(Path.GetTempPath(), "LambContentRoundTrip", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var phone = Path.Combine(root, "phone", "user");
            var staged = Path.Combine(root, "cache", "shizuku-user");
            var pc = Path.Combine(root, "pc", "saves");
            Directory.CreateDirectory(phone);
            Directory.CreateDirectory(staged);
            Directory.CreateDirectory(pc);
            var plugin = new CultOfTheLambSavePlugin();
            WriteProgress(phone, 17, GameSaveTargetPlatform.AndroidPort);
            File.WriteAllText(Path.Combine(phone, "settings"), "phone-settings");
            File.WriteAllText(Path.Combine(pc, "settings.json"), "pc-settings");
            foreach (var file in Directory.GetFiles(phone)) File.Copy(file, Path.Combine(staged, Path.GetFileName(file)));

            var config = Config("咩咩启示录", Unit(0, "pc", true), Unit(1, "android", true));
            config.Games[0].SavePaths[0].Paths["pc"] = pc;
            config.Games[0].SavePaths[1].Paths["android"] = phone;
            Assert.True(MobileConfigNormalizer.Normalize(config));
            var configPath = Path.Combine(root, "GameSaveManager.config.json");
            var configurationService = new GameSaveManagerConfigurationService();
            await configurationService.SaveAsync(configPath, config);
            config = await configurationService.LoadAsync(configPath);
            Assert.True(Assert.Single(config.Games[0].SavePaths).ContentsOnly);

            var library = new GameLibraryService(new EnvironmentTokenResolver(), new CurrentDeviceService());
            var archives = new ArchiveTransferService();
            var unit = config.Games[0].SavePaths[0];
            // Match the MAUI upload flow: temporarily redirect the configured path,
            // rebuild the snapshot and apply the real Android scoping factory.
            unit.Paths["android"] = staged;
            var androidSnapshot = library.CreateSnapshot(config, configPath, "Android", "android");
            var androidGame = AndroidGameSnapshotFactory.Create(androidSnapshot.Games[0], config.Games[0], "android");
            var androidArchive = archives.CreateCurrentDeviceArchive(androidGame, androidSnapshot.CurrentDevice, Path.Combine(root, "android-upload"));
            unit.Paths["android"] = phone;
            AssertContentLayout(androidArchive);

            var pcSnapshot = library.CreateSnapshot(config, configPath, "PC", "windows");
            archives.RestoreCurrentDeviceArchive(androidArchive, pcSnapshot.Games[0], pcSnapshot.CurrentDevice);
            AssertProgress(pc, 17, pcFormat: true);
            Assert.Equal("pc-settings", File.ReadAllText(Path.Combine(pc, "settings.json")));

            WriteProgress(pc, 18, GameSaveTargetPlatform.SteamPc);
            var pcArchive = archives.CreateCurrentDeviceArchive(pcSnapshot.Games[0], pcSnapshot.CurrentDevice, Path.Combine(root, "pc-upload"));
            AssertContentLayout(pcArchive);
            androidSnapshot = library.CreateSnapshot(config, configPath, "Android", "android");
            androidGame = AndroidGameSnapshotFactory.Create(androidSnapshot.Games[0], config.Games[0], "android");
            archives.RestoreCurrentDeviceArchive(pcArchive, androidGame, androidSnapshot.CurrentDevice,
                targetPlatformOverride: GameSaveTargetPlatform.AndroidPort);
            AssertProgress(phone, 18, pcFormat: false);
            Assert.Equal("phone-settings", File.ReadAllText(Path.Combine(phone, "settings")));
            Assert.False(Directory.Exists(Path.Combine(pc, "user")));
            Assert.False(Directory.Exists(Path.Combine(pc, "content")));
            Assert.False(Directory.Exists(Path.Combine(phone, "saves")));
            Assert.False(Directory.Exists(Path.Combine(phone, "content")));

            void WriteProgress(string directory, int day, GameSaveTargetPlatform target)
            {
                foreach (var pair in new[] { ("slot_0", $"{{\"CurrentDayIndex\":{day}}}"), ("meta_0", $"{{\"Day\":{day},\"Version\":\"1.4.14\"}}") })
                    File.WriteAllBytes(Path.Combine(directory, plugin.ToTargetName(pair.Item1, target)),
                        plugin.FromCanonical(Encoding.UTF8.GetBytes(pair.Item2), target));
            }
            void AssertProgress(string directory, int day, bool pcFormat)
            {
                foreach (var name in new[] { "slot_0", "meta_0" })
                {
                    var raw = File.ReadAllBytes(Path.Combine(directory, name + (pcFormat ? ".json" : "")));
                    Assert.Equal(pcFormat ? (byte)'E' : (byte)'Z', raw[0]);
                    var canonical = CultOfTheLambSavePlugin.ToCanonicalBytes(name, raw);
                    using var json = JsonDocument.Parse(canonical);
                    Assert.Equal(day, json.RootElement.GetProperty(name == "slot_0" ? "CurrentDayIndex" : "Day").GetInt32());
                }
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void AssertContentLayout(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(new[] { "0/content/meta_0", "0/content/slot_0" }, zip.Entries.Select(entry => entry.FullName).Order());
    }

    private static SaveUnitDefinition Unit(int id, string deviceId, bool contentsOnly) => new()
    {
        Id = id, UnitType = SaveUnitType.Folder, ContentsOnly = contentsOnly,
        Exclude = ["settings*", "Prefs.dict"], Paths = new() { [deviceId] = "/configured/" + deviceId }
    };

    private static ManagerConfig Config(string name, params SaveUnitDefinition[] units) => new()
    {
        QuickAction = new QuickActionSettings { QuickActionGame = JsonSerializer.SerializeToElement<object?>(null) },
        Games = [new GameDefinition { Name = name, SavePaths = [.. units], NextSaveUnitId = units.Max(unit => unit.Id) + 1 }],
        Devices = new()
        {
            ["pc"] = new DeviceDefinition { Id = "pc", Name = "PC", Platform = "windows" },
            ["android"] = new DeviceDefinition { Id = "android", Name = "Android", Platform = "android" }
        }
    };
}
