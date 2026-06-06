using System.IO.Compression;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services;

namespace EflayGameSaveManager.Core.Tests;

public sealed class CurrentDeviceAndArchiveTests
{
    [Fact]
    public void EnsureCurrentDevice_AddsMissingDeviceAndCopiesDefaultPaths()
    {
        var config = new ManagerConfig
        {
            Devices = new Dictionary<string, DeviceDefinition>
            {
                ["device-a"] = new()
                {
                    Id = "device-a",
                    Name = "DESKTOP-A"
                }
            },
            Games =
            [
                new GameDefinition
                {
                    Name = "Test Game",
                    SavePaths =
                    [
                        new SaveUnitDefinition
                        {
                            Id = 0,
                            UnitType = SaveUnitType.Folder,
                            Paths = new Dictionary<string, string>
                            {
                                ["device-a"] = @"C:\SaveA"
                            }
                        }
                    ],
                    GamePaths = new Dictionary<string, string>
                    {
                        ["device-a"] = @"D:\Games\TestGame.exe"
                    }
                }
            ]
        };

        var service = new CurrentDeviceService();

        var device = service.EnsureCurrentDevice(config, "DESKTOP-B");

        Assert.True(device.WasAdded);
        Assert.Equal("DESKTOP-B", device.DeviceName);
        Assert.True(config.Devices.ContainsKey(device.DeviceId));
        Assert.False(config.Games[0].SavePaths[0].Paths.ContainsKey(device.DeviceId));
        Assert.False(config.Games[0].GamePaths.ContainsKey(device.DeviceId));
        Assert.Equal(@"C:\SaveA", service.GetCurrentDevicePaths(config.Games[0], device.DeviceId)[0].Path);
    }

    [Fact]
    public void RestoreCurrentDeviceArchive_RestoresLegacyZipLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "EflayGameSaveManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var archivePath = Path.Combine(root, "legacy.zip");
            var zipSource = Path.Combine(root, "zip-source");
            var unitSource = Path.Combine(zipSource, "0", "SAVEDATA");
            Directory.CreateDirectory(unitSource);
            File.WriteAllText(Path.Combine(unitSource, "slot1.sav"), "cloud-data");
            ZipFile.CreateFromDirectory(zipSource, archivePath);

            var restoreTarget = Path.Combine(root, "restored");
            var game = new GameSnapshot(
                "WRC9",
                true,
                [
                    new ResolvedSaveUnit(
                        0,
                        SaveUnitType.Folder,
                        true,
                        [
                            new ResolvedDevicePath("device-a", "DESKTOP-A", restoreTarget)
                        ])
                ],
                [],
                [
                    new CurrentDevicePathInfo(0, SaveUnitType.Folder, restoreTarget, true)
                ],
                new CurrentDeviceGamePathInfo(string.Empty));

            var service = new ArchiveTransferService();

            service.RestoreCurrentDeviceArchive(archivePath, game, new CurrentDeviceContext("device-a", "DESKTOP-A", false));

            Assert.True(File.Exists(Path.Combine(restoreTarget, "SAVEDATA", "slot1.sav")));
            Assert.Equal("cloud-data", File.ReadAllText(Path.Combine(restoreTarget, "SAVEDATA", "slot1.sav")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateCurrentDeviceArchive_WritesBasicRgsmV2LayoutAndComment()
    {
        var root = Path.Combine(Path.GetTempPath(), "EflayGameSaveManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var saveRoot = Path.Combine(root, "SAVEDATA");
            Directory.CreateDirectory(saveRoot);
            File.WriteAllText(Path.Combine(saveRoot, "slot1.sav"), "local-data");

            var game = new GameSnapshot(
                "WRC9",
                true,
                [
                    new ResolvedSaveUnit(
                        0,
                        SaveUnitType.Folder,
                        true,
                        [
                            new ResolvedDevicePath("device-a", "DESKTOP-A", saveRoot)
                        ])
                ],
                [],
                [
                    new CurrentDevicePathInfo(0, SaveUnitType.Folder, saveRoot, true)
                ],
                new CurrentDeviceGamePathInfo(string.Empty));

            var service = new ArchiveTransferService();
            var archivePath = service.CreateCurrentDeviceArchive(game, new CurrentDeviceContext("device-a", "DESKTOP-A", false), root);

            using var archive = ZipFile.OpenRead(archivePath);
            Assert.Contains(archive.Entries, entry => string.Equals(entry.FullName.Replace('\\', '/'), "0/SAVEDATA/slot1.sav", StringComparison.Ordinal));
            Assert.StartsWith("RGSM_ARCHIVE_V2", ReadZipComment(archivePath), StringComparison.Ordinal);
            Assert.Contains("\"compression\":\"deflate\"", ReadZipComment(archivePath), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string ReadZipComment(string archivePath)
    {
        var bytes = File.ReadAllBytes(archivePath);
        for (var index = bytes.Length - 22; index >= 0; index--)
        {
            if (bytes[index] != 0x50 ||
                bytes[index + 1] != 0x4b ||
                bytes[index + 2] != 0x05 ||
                bytes[index + 3] != 0x06)
            {
                continue;
            }

            var commentLength = bytes[index + 20] | (bytes[index + 21] << 8);
            return System.Text.Encoding.UTF8.GetString(bytes, index + 22, commentLength);
        }

        return string.Empty;
    }

    [Fact]
    public void RestoreCurrentDeviceArchive_RestoresRootLevelArchiveLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "EflayGameSaveManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var archivePath = Path.Combine(root, "root-layout.zip");
            var zipSource = Path.Combine(root, "zip-source");
            var saveDataRoot = Path.Combine(zipSource, "SAVEDATA");
            Directory.CreateDirectory(saveDataRoot);
            File.WriteAllText(Path.Combine(saveDataRoot, "slot1.sav"), "cloud-root-layout");
            ZipFile.CreateFromDirectory(zipSource, archivePath);

            var restoreTarget = Path.Combine(root, "restored");
            var game = new GameSnapshot(
                "WRC9",
                true,
                [
                    new ResolvedSaveUnit(
                        0,
                        SaveUnitType.Folder,
                        true,
                        [
                            new ResolvedDevicePath("device-a", "DESKTOP-A", restoreTarget)
                        ])
                ],
                [],
                [
                    new CurrentDevicePathInfo(0, SaveUnitType.Folder, restoreTarget, true)
                ],
                new CurrentDeviceGamePathInfo(string.Empty));

            var service = new ArchiveTransferService();

            service.RestoreCurrentDeviceArchive(archivePath, game, new CurrentDeviceContext("device-a", "DESKTOP-A", false));

            Assert.True(File.Exists(Path.Combine(restoreTarget, "SAVEDATA", "slot1.sav")));
            Assert.Equal("cloud-root-layout", File.ReadAllText(Path.Combine(restoreTarget, "SAVEDATA", "slot1.sav")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void RestoreCurrentDeviceArchive_ZipUnitRepackagesSingleNumericRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "EflayGameSaveManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var archivePath = Path.Combine(root, "wrapped.zip");
            var zipSource = Path.Combine(root, "zip-source");
            var saveDataRoot = Path.Combine(zipSource, "0", "SAVEDATA");
            Directory.CreateDirectory(saveDataRoot);
            File.WriteAllText(Path.Combine(saveDataRoot, "slot1.sav"), "zip-data");
            ZipFile.CreateFromDirectory(zipSource, archivePath);

            var restoreTarget = Path.Combine(root, "restored.zip");
            var game = new GameSnapshot(
                "SwitchGame",
                true,
                [
                    new ResolvedSaveUnit(
                        1,
                        SaveUnitType.Zip,
                        false,
                        [
                            new ResolvedDevicePath("device-a", "Android", restoreTarget)
                        ])
                ],
                [],
                [
                    new CurrentDevicePathInfo(1, SaveUnitType.Zip, restoreTarget, false)
                ],
                new CurrentDeviceGamePathInfo(string.Empty));

            var service = new ArchiveTransferService();

            service.RestoreCurrentDeviceArchive(archivePath, game, new CurrentDeviceContext("device-a", "Android", false));

            using var restoredArchive = ZipFile.OpenRead(restoreTarget);
            Assert.DoesNotContain(restoredArchive.Entries, entry => entry.FullName.StartsWith("0/", StringComparison.Ordinal));
            Assert.Contains(restoredArchive.Entries, entry => string.Equals(entry.FullName.Replace('\\', '/'), "SAVEDATA/slot1.sav", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void RestoreCurrentDeviceArchive_UnwrapsSingleSameNamedFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "EflayGameSaveManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var archivePath = Path.Combine(root, "wrapped.zip");
            var zipSource = Path.Combine(root, "zip-source");
            var wrappedRoot = Path.Combine(zipSource, "MySaves");
            Directory.CreateDirectory(wrappedRoot);
            File.WriteAllText(Path.Combine(wrappedRoot, "slot1.sav"), "wrapped-data");
            ZipFile.CreateFromDirectory(zipSource, archivePath);

            var restoreTarget = Path.Combine(root, "MySaves");
            var game = new GameSnapshot(
                "WrappedGame",
                true,
                [
                    new ResolvedSaveUnit(
                        0,
                        SaveUnitType.Folder,
                        true,
                        [
                            new ResolvedDevicePath("device-a", "DESKTOP-A", restoreTarget)
                        ])
                ],
                [],
                [
                    new CurrentDevicePathInfo(0, SaveUnitType.Folder, restoreTarget, true)
                ],
                new CurrentDeviceGamePathInfo(string.Empty));

            var service = new ArchiveTransferService();

            service.RestoreCurrentDeviceArchive(archivePath, game, new CurrentDeviceContext("device-a", "DESKTOP-A", false));

            Assert.True(File.Exists(Path.Combine(restoreTarget, "slot1.sav")));
            Assert.False(Directory.Exists(Path.Combine(restoreTarget, "MySaves")));
            Assert.Equal("wrapped-data", File.ReadAllText(Path.Combine(restoreTarget, "slot1.sav")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveBackupService_BackupAsync_KeepsFolderBaseDirectoryUnderStableUnitId()
    {
        var root = Path.Combine(Path.GetTempPath(), "EflayGameSaveManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var saveRoot = Path.Combine(root, "SAVEDATA");
            Directory.CreateDirectory(saveRoot);
            File.WriteAllText(Path.Combine(saveRoot, "slot1.sav"), "local-data");

            var game = new GameSnapshot(
                "WRC9",
                true,
                [
                    new ResolvedSaveUnit(
                        7,
                        SaveUnitType.Folder,
                        true,
                        [
                            new ResolvedDevicePath("device-a", "DESKTOP-A", saveRoot)
                        ])
                ],
                [],
                [
                    new CurrentDevicePathInfo(7, SaveUnitType.Folder, saveRoot, true)
                ],
                new CurrentDeviceGamePathInfo(string.Empty));

            var backupRoot = Path.Combine(root, "backups");
            var service = new SaveBackupService();

            var backupPath = await service.BackupAsync(game, backupRoot);

            Assert.True(Directory.Exists(backupPath));
            Assert.True(File.Exists(Path.Combine(backupPath, "DESKTOP-A", "7", "SAVEDATA", "slot1.sav")));
            Assert.False(File.Exists(Path.Combine(backupPath, "DESKTOP-A", "7", "slot1.sav")));
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
