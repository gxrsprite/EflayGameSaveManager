using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services;
using EflayGameSaveManager.Maui.Services;

namespace EflayGameSaveManager.Core.Tests;

public class AndroidFolderRestoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestrictedSavedataRestore_KeepsGameFilesAtRoot(bool contentsOnly)
    {
        var root = Path.Combine(Path.GetTempPath(), "gow3-restore-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source", "savedata");
        Directory.CreateDirectory(Path.Combine(source, "BCES00510"));
        File.WriteAllText(Path.Combine(source, "BCES00510", "DATA.BIN"), "current-progress");
        try
        {
            var device = new CurrentDeviceContext("android", "Android", false, AppPlatform.Android);
            var service = new ArchiveTransferService();
            var archive = service.CreateCurrentDeviceArchive(Snapshot(source, contentsOnly), device, Path.Combine(root, "backup"));
            var staged = AndroidGameSnapshotFactory.GetRestoreStagingPath(Path.Combine(root, "restore"), 0,
                SaveUnitType.Folder, "/data/data/com.gow3.android.bionic/files/savedata/");
            service.RestoreCurrentDeviceArchive(archive, Snapshot(staged, contentsOnly), device);
            Assert.Equal("savedata", Path.GetFileName(staged));
            Assert.Equal("current-progress", File.ReadAllText(Path.Combine(staged, "BCES00510", "DATA.BIN")));
            Assert.False(Directory.Exists(Path.Combine(staged, "savedata")));
            Assert.False(Directory.Exists(Path.Combine(staged, "content")));
            Assert.Single(Directory.GetFiles(staged, "*", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void StagingPaths_IsolateUnits_AndKeepFileTargetsAsFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "restore-staging");
        var first = AndroidGameSnapshotFactory.GetRestoreStagingPath(root, 0, SaveUnitType.Folder, "/data/data/game/savedata");
        var second = AndroidGameSnapshotFactory.GetRestoreStagingPath(root, 1, SaveUnitType.Folder, "/data/data/other/savedata");
        Assert.NotEqual(first, second);
        Assert.Equal(Path.Combine(root, "unit-0", "savedata"), first);
        Assert.Equal(Path.Combine(root, "unit-2"), AndroidGameSnapshotFactory.GetRestoreStagingPath(root, 2, SaveUnitType.File, "/data/data/game/save.bin"));
    }

    private static GameSnapshot Snapshot(string path, bool contentsOnly) => new("战神3", true,
        [new ResolvedSaveUnit(0, SaveUnitType.Folder, false, [new ResolvedDevicePath("android", "Android", path)], ContentsOnly: contentsOnly)],
        [], [new CurrentDevicePathInfo(0, SaveUnitType.Folder, path, false)], new CurrentDeviceGamePathInfo(string.Empty), "战神3");
}
