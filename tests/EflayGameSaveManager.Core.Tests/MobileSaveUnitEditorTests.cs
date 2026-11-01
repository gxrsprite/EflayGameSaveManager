using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services;
using EflayGameSaveManager.Maui.Services;

namespace EflayGameSaveManager.Core.Tests;

public class MobileSaveUnitEditorTests
{
    [Fact]
    public async Task EnableZip_RetainsPcFolder_AndPersistsDocumentUri()
    {
        var folder = new SaveUnitDefinition { Id = 0, UnitType = SaveUnitType.Folder,
            ContentsOnly = true, Exclude = ["settings*"], Paths = new() { ["pc"] = @"C:\saves", ["android"] = "/data/data/game/files" } };
        var game = new GameDefinition { Name = "Test", SavePaths = [folder], NextSaveUnitId = 1 };
        var zip = MobileSaveUnitEditor.Apply(game, "android", 0, SaveUnitType.Zip, "content://provider/document/save.zip", [], true);
        Assert.Equal(SaveUnitType.Folder, folder.UnitType);
        Assert.Equal(@"C:\saves", folder.Paths["pc"]);
        Assert.False(folder.Paths.ContainsKey("android"));
        Assert.True(folder.ContentsOnly);
        Assert.Equal(new[] { "settings*" }, folder.Exclude);
        Assert.Contains(zip.Id, folder.LinkedUnitIds);
        Assert.Contains(folder.Id, zip.LinkedUnitIds);
        Assert.True(zip.ContentsOnly);
        Assert.Same(zip, MobileSaveUnitEditor.FindZip(game, "android"));
        Assert.Null(MobileSaveUnitEditor.FindZip(game, "pc"));
        var path = Path.Combine(Path.GetTempPath(), "zip-config-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var service = new GameSaveManagerConfigurationService();
            await service.SaveAsync(path, new ManagerConfig { Games = [game], QuickAction = new QuickActionSettings
                { QuickActionGame = System.Text.Json.JsonSerializer.SerializeToElement<object?>(null) } });
            var config = await service.LoadAsync(path);
            var loaded = MobileSaveUnitEditor.FindZip(config.Games[0], "android")!;
            Assert.Equal(SaveUnitType.Zip, loaded.UnitType);
            Assert.True(loaded.ContentsOnly);
            MobileConfigNormalizer.Normalize(config);
            Assert.True(MobileSaveUnitEditor.FindZip(config.Games[0], "android")!.ContentsOnly);
            Assert.Equal("content://provider/document/save.zip", loaded.Paths["android"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ToggleModesBackAndForth_ReusesLinkedUnitsWithoutLosingPcPath()
    {
        var game = new GameDefinition { Name = "Test", NextSaveUnitId = 1, SavePaths = [new SaveUnitDefinition
            { Id = 0, UnitType = SaveUnitType.Folder, Paths = new() { ["pc"] = @"C:\saves", ["android"] = "/data/data/game" } }] };
        var zip = MobileSaveUnitEditor.Apply(game, "android", 0, SaveUnitType.Zip, "/storage/emulated/0/save.zip", [], false);
        var folder = MobileSaveUnitEditor.Apply(game, "android", zip.Id, SaveUnitType.Folder, "/data/data/game", [], true);
        Assert.Equal(0, folder.Id);
        Assert.Equal(@"C:\saves", folder.Paths["pc"]);
        Assert.False(zip.Paths.ContainsKey("android"));
        Assert.Null(MobileSaveUnitEditor.FindZip(game, "android"));
        var again = MobileSaveUnitEditor.Apply(game, "android", folder.Id, SaveUnitType.Zip, "/data/data/game/save.zip", [], false);
        Assert.Equal(zip.Id, again.Id);
        Assert.Equal(2, game.SavePaths.Count);
        Assert.Equal(new[] { zip.Id }, folder.LinkedUnitIds);
    }
}
