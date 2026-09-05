using System.IO.Compression;
using System.Text.Json;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services;

namespace EflayGameSaveManager.Core.Tests;

public sealed class SaveMigrationTests
{
    [Fact]
    public async Task MigrationPackage_RestoresAllGamesAndCreatesTargetDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "EflayGameSaveManager.Tests", Guid.NewGuid().ToString("N"));
        var sourceSave = Path.Combine(root, "source", "GameA", "Saves");
        var targetSave = Path.Combine(root, "target", "GameA", "Saves");
        var configPath = Path.Combine(root, "GameSaveManager.config.json");
        var packagePath = Path.Combine(root, "migration.zip");
        Directory.CreateDirectory(sourceSave);
        File.WriteAllText(Path.Combine(sourceSave, "slot1.sav"), "migration-data");

        var config = new ManagerConfig
        {
            BackupPath = "./save_data",
            QuickAction = new QuickActionSettings
            {
                QuickActionGame = JsonDocument.Parse("null").RootElement.Clone()
            },
            Devices = new Dictionary<string, DeviceDefinition>
            {
                ["source"] = new() { Id = "source", Name = "SOURCE" },
                ["target"] = new() { Id = "target", Name = "TARGET" }
            },
            Games =
            [
                new GameDefinition
                {
                    Name = "Game A",
                    SavePaths =
                    [
                        new SaveUnitDefinition
                        {
                            Id = 0,
                            UnitType = SaveUnitType.Folder,
                            Paths = new Dictionary<string, string>
                            {
                                ["source"] = sourceSave,
                                ["target"] = targetSave
                            }
                        }
                    ]
                }
            ]
        };

        try
        {
            var configurationService = new GameSaveManagerConfigurationService();
            await configurationService.SaveAsync(configPath, config);
            var currentDeviceService = new CurrentDeviceService();
            var libraryService = new GameLibraryService(
                new EnvironmentTokenResolver(),
                currentDeviceService,
                new AppRuntimeSettingsService());
            var snapshot = libraryService.CreateSnapshot(config, configPath, "SOURCE");
            var migrationService = new SaveMigrationService(
                new ArchiveTransferService(),
                configurationService,
                libraryService);

            var export = await migrationService.CreatePackageAsync(config, snapshot, packagePath);

            Assert.Equal(1, export.ProcessedGames);
            Assert.True(File.Exists(packagePath));
            using (var archive = ZipFile.OpenRead(packagePath))
            {
                Assert.Contains(archive.Entries, entry => entry.FullName == "config/GameSaveManager.config.json");
                Assert.Contains(archive.Entries, entry => entry.FullName == "migration-manifest.json");
            }

            var restore = await migrationService.RestorePackageAsync(packagePath, configPath, "TARGET");

            Assert.Equal(1, restore.ProcessedGames);
            Assert.Equal("migration-data", File.ReadAllText(Path.Combine(targetSave, "slot1.sav")));
            Assert.NotEmpty(Directory.GetFiles(root, "GameSaveManager.config.json.before-migration-*.bak"));
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
