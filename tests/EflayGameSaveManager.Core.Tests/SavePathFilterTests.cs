using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services;

namespace EflayGameSaveManager.Core.Tests;

public sealed class SavePathFilterTests
{
    [Theory]
    [InlineData("settings.json", "settings*", true)]
    [InlineData("settings", "settings*", true)]
    [InlineData("slot_0", "settings*", false)]
    [InlineData("Prefs.dict", "Prefs.dict", true)]
    [InlineData("prefs.DICT", "Prefs.dict", true)]
    [InlineData("save.dat", "*.dat", true)]
    [InlineData("save.dat", "*.log", false)]
    [InlineData("meta_0.json", "slot_?", false)]
    [InlineData("slot_0", "slot_?", true)]
    [InlineData("slot_0.json", "slot_?", false)]
    [InlineData("slot_0.json", "slot_*", true)]
    [InlineData("anything", "sub/file.txt", false)]
    public void IsExcluded_MatchesNameAndGlob(string fileName, string pattern, bool expected)
    {
        Assert.Equal(expected, SavePathFilter.IsExcluded(fileName, fileName, [pattern]));
    }

    [Fact]
    public void IsExcluded_DirectoryPatternExcludesSubtree()
    {
        Assert.True(SavePathFilter.IsExcluded("a.txt", "cache/a.txt", ["cache/"]));
        Assert.True(SavePathFilter.IsExcluded("cache", "cache/", ["cache/"]));
        Assert.False(SavePathFilter.IsExcluded("a.txt", "saves/a.txt", ["cache/"]));
    }

    [Fact]
    public void IsExcluded_IgnoresBlankAndCommentPatterns()
    {
        Assert.False(SavePathFilter.IsExcluded("settings.json", "settings.json", ["", "   ", "# keep me"]));
    }

    [Fact]
    public void Backup_OmitsExcludedFiles()
    {
        var root = NewTempDirectory();
        try
        {
            var source = Path.Combine(root, "saves");
            var staging = Path.Combine(root, "staging", "0");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "slot_0"), "progress");
            File.WriteAllText(Path.Combine(source, "meta_0"), "meta");
            File.WriteAllText(Path.Combine(source, "settings"), "device-local");
            File.WriteAllText(Path.Combine(source, "Prefs.dict"), "prefs");

            var game = FolderGame(source, ["settings*", "Prefs.dict"]);
            var service = new ArchiveTransferService();
            var archive = service.CreateCurrentDeviceArchive(game, Device(), Path.Combine(root, "work"));

            var entries = ReadArchiveEntries(archive);
            // Unit 0 stages the source folder with its base directory name.
            Assert.Contains("0/saves/slot_0", entries);
            Assert.Contains("0/saves/meta_0", entries);
            Assert.DoesNotContain(entries, entry => entry.Contains("settings"));
            Assert.DoesNotContain(entries, entry => entry.Contains("Prefs.dict"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Restore_DoesNotOverwriteExcludedFiles()
    {
        var root = NewTempDirectory();
        try
        {
            // Source and target share the "saves" leaf name, as real save paths do.
            var source = Path.Combine(root, "a", "saves");
            var target = Path.Combine(root, "b", "saves");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(source, "slot_0"), "from-cloud");
            File.WriteAllText(Path.Combine(source, "settings"), "device-local");
            File.WriteAllText(Path.Combine(target, "settings"), "keep-me");

            var game = FolderGame(target, ["settings*"]);
            var service = new ArchiveTransferService();

            var archive = service.CreateCurrentDeviceArchive(
                FolderGame(source, []),
                Device(),
                Path.Combine(root, "work"));

            // Delete-before-apply must still preserve the excluded local file.
            service.RestoreCurrentDeviceArchive(archive, game with
            {
                SaveUnits = [game.SaveUnits[0] with { DeleteBeforeApply = true }]
            }, Device());

            Assert.Equal("from-cloud", File.ReadAllText(Path.Combine(target, "slot_0")));
            Assert.Equal("keep-me", File.ReadAllText(Path.Combine(target, "settings")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Restore_LeavesExcludedFileUntouchedWhenArchiveContainsIt()
    {
        var root = NewTempDirectory();
        try
        {
            var source = Path.Combine(root, "a", "saves");
            var target = Path.Combine(root, "b", "saves");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(source, "slot_0"), "from-cloud");
            File.WriteAllText(Path.Combine(source, "settings"), "remote-settings");
            File.WriteAllText(Path.Combine(target, "settings"), "keep-me");

            var service = new ArchiveTransferService();
            var archive = service.CreateCurrentDeviceArchive(
                FolderGame(source, []),
                Device(),
                Path.Combine(root, "work"));

            service.RestoreCurrentDeviceArchive(archive, FolderGame(target, ["settings*"]), Device());

            Assert.Equal("from-cloud", File.ReadAllText(Path.Combine(target, "slot_0")));
            Assert.Equal("keep-me", File.ReadAllText(Path.Combine(target, "settings")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Config_RoundTripsExcludePatterns()
    {
        var root = NewTempDirectory();
        try
        {
            var configPath = Path.Combine(root, "GameSaveManager.config.json");
            var service = new GameSaveManagerConfigurationService();

            var config = service.CreateDefault();
            config.Games.Add(new GameDefinition
            {
                Name = "咩咩启示录",
                SavePaths =
                [
                    new SaveUnitDefinition
                    {
                        Id = 0,
                        UnitType = SaveUnitType.Folder,
                        Exclude = ["settings*", "Prefs.dict"],
                        Paths = new Dictionary<string, string> { ["dev-1"] = @"C:\saves" }
                    }
                ]
            });

            await service.SaveAsync(configPath, config);
            var reloaded = await service.LoadAsync(configPath);

            var unit = reloaded.Games.Single(game => game.Name == "咩咩启示录").SavePaths[0];
            Assert.Equal(["settings*", "Prefs.dict"], unit.Exclude);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ContentsOnly_IgnoresFolderNameOnBothSides()
    {
        var root = NewTempDirectory();
        try
        {
            // PC uses "...\saves", the Android port uses ".../user".
            var pcSaves = Path.Combine(root, "pc", "saves");
            var phoneUser = Path.Combine(root, "phone", "user");
            Directory.CreateDirectory(pcSaves);
            Directory.CreateDirectory(phoneUser);
            File.WriteAllText(Path.Combine(pcSaves, "slot_0"), "from-pc");
            File.WriteAllText(Path.Combine(pcSaves, "meta_0"), "meta");

            var service = new ArchiveTransferService();
            var archive = service.CreateCurrentDeviceArchive(
                FolderGame(pcSaves, [], contentsOnly: true),
                Device(),
                Path.Combine(root, "work"));

            using (var zip = System.IO.Compression.ZipFile.OpenRead(archive))
            {
                var names = zip.Entries.Select(entry => entry.FullName).ToArray();
                Assert.Contains("0/content/slot_0", names);
                Assert.Contains("0/content/meta_0", names);
                Assert.DoesNotContain(names, name => name.Contains("saves"));
            }

            service.RestoreCurrentDeviceArchive(archive, FolderGame(phoneUser, [], contentsOnly: true), Device());

            Assert.Equal("from-pc", File.ReadAllText(Path.Combine(phoneUser, "slot_0")));
            Assert.Equal("meta", File.ReadAllText(Path.Combine(phoneUser, "meta_0")));
            Assert.False(Directory.Exists(Path.Combine(phoneUser, "saves")));
            Assert.False(Directory.Exists(Path.Combine(phoneUser, "0")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Restore_ContentWrapperNeverBecomesTargetSubfolder()
    {
        var root = NewTempDirectory();
        try
        {
            var source = Path.Combine(root, "src", "saves");
            var target = Path.Combine(root, "phone", "user");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(source, "slot_0"), "canonical");

            var service = new ArchiveTransferService();
            var archive = service.CreateCurrentDeviceArchive(
                FolderGame(source, [], contentsOnly: true),
                Device(),
                Path.Combine(root, "work"));

            // Even if the target unit is configured as folder mode, a "content" wrapper
            // must be unwrapped so files land directly in the configured folder.
            service.RestoreCurrentDeviceArchive(archive, FolderGame(target, [], contentsOnly: false), Device());

            Assert.Equal("canonical", File.ReadAllText(Path.Combine(target, "slot_0")));
            Assert.False(Directory.Exists(Path.Combine(target, "content")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Restore_ContentsOnlyUnwrapsLegacyUserFolder()
    {
        var root = NewTempDirectory();
        try
        {
            var archive = Path.Combine(root, "phone.zip");
            var extract = Path.Combine(root, "0", "user");
            var target = Path.Combine(root, "pc", "saves");
            Directory.CreateDirectory(extract);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(extract, "slot_0"), "from-phone");
            System.IO.Compression.ZipFile.CreateFromDirectory(
                Path.Combine(root, "0"), archive,
                System.IO.Compression.CompressionLevel.NoCompression,
                includeBaseDirectory: false);

            new ArchiveTransferService().RestoreCurrentDeviceArchive(
                archive,
                FolderGame(target, [], contentsOnly: true),
                Device());

            Assert.Equal("from-phone", File.ReadAllText(Path.Combine(target, "slot_0")));
            Assert.False(Directory.Exists(Path.Combine(target, "user")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FolderMode_PreservesOriginalFolderName()
    {
        var root = NewTempDirectory();
        try
        {
            var source = Path.Combine(root, "saves");
            var target = Path.Combine(root, "user");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(source, "slot_0"), "progress");

            var service = new ArchiveTransferService();
            var archive = service.CreateCurrentDeviceArchive(
                FolderGame(source, [], contentsOnly: false),
                Device(),
                Path.Combine(root, "work"));

            using (var zip = System.IO.Compression.ZipFile.OpenRead(archive))
                Assert.Contains("0/saves/slot_0", zip.Entries.Select(entry => entry.FullName));

            service.RestoreCurrentDeviceArchive(archive, FolderGame(target, [], contentsOnly: false), Device());
            Assert.Equal("progress", File.ReadAllText(Path.Combine(target, "saves", "slot_0")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static GameSnapshot FolderGame(
        string path,
        IReadOnlyList<string> exclude,
        bool contentsOnly = false) =>
        new(
            "Test Game",
            true,
            [
                new ResolvedSaveUnit(
                    0,
                    SaveUnitType.Folder,
                    false,
                    [new ResolvedDevicePath("dev-1", "PC", path)],
                    exclude,
                    contentsOnly)
            ],
            [],
            [new CurrentDevicePathInfo(0, SaveUnitType.Folder, path, false)],
            new CurrentDeviceGamePathInfo(string.Empty),
            string.Empty);

    private static CurrentDeviceContext Device() => new("dev-1", "PC", false);

    private static IReadOnlyList<string> ReadArchiveEntries(string archivePath)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(archivePath);
        return archive.Entries.Select(entry => entry.FullName).ToArray();
    }

    private static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "rgsm-filter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
