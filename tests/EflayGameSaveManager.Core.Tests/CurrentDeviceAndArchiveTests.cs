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
}
