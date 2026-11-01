using System.IO.Compression;
using System.Security.Cryptography;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services;

namespace EflayGameSaveManager.Core.Tests;

public sealed class ZipContentsOnlyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "zip-layout-" + Guid.NewGuid().ToString("N"));
    private readonly ArchiveTransferService _service = new();
    private static readonly CurrentDeviceContext Device = new("device", "Test", false);
    private const string Payload = "1/CUSA01740/AUTOSAVE/SAVEDATA";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FolderToZip_UsesDestinationContentSwitch(bool sourceContents, bool targetContents)
    {
        var source = MakeFolder();
        var cloud = _service.CreateCurrentDeviceArchive(Snapshot(source, SaveUnitType.Folder, sourceContents), Device, Work("cloud"));
        var target = Path.Combine(_root, "export.zip");
        _service.RestoreCurrentDeviceArchive(cloud, Snapshot(target, SaveUnitType.Zip, targetContents), Device);
        AssertZip(target, targetContents ? Payload : "savedata/" + Payload);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ZipToFolder_HonorsBothModes_WithDifferentDestinationName(bool sourceContents, bool targetContents)
    {
        var input = MakeZip();
        var cloud = _service.CreateCurrentDeviceArchive(Snapshot(input, SaveUnitType.Zip, sourceContents), Device, Work("cloud"));
        var target = Path.Combine(_root, "phone-dir");
        _service.RestoreCurrentDeviceArchive(cloud, Snapshot(target, SaveUnitType.Folder, targetContents), Device);
        var relative = sourceContents || targetContents ? Payload : "savedata/" + Payload;
        Assert.Equal("progress", File.ReadAllText(Path.Combine(target, relative)));
        Assert.Single(Directory.GetFiles(target, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ZipToZip_RepeatedTransfersNeverStripNumericGameDirectory(bool sourceContents, bool targetContents)
    {
        var input = MakeZip();
        var original = SHA256.HashData(File.ReadAllBytes(input));
        var cloud = _service.CreateCurrentDeviceArchive(Snapshot(input, SaveUnitType.Zip, sourceContents), Device, Work("initial"));
        var target = Path.Combine(_root, "mobile.zip");
        var game = Snapshot(target, SaveUnitType.Zip, targetContents, 1);
        for (var i = 0; i < 3; i++)
        {
            _service.RestoreCurrentDeviceArchive(cloud, game, Device);
            AssertZip(target, targetContents ? Payload : "savedata/" + Payload);
            cloud = _service.CreateCurrentDeviceArchive(game, Device, Work("cycle-" + i));
        }
        Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(input)));
    }

    [Fact]
    public void SwitchingZipMode_ReconstructsOriginalFolderName()
    {
        var input = MakeZip();
        var cloud = _service.CreateCurrentDeviceArchive(Snapshot(input, SaveUnitType.Zip, true), Device, Work("first"));
        var target = Path.Combine(_root, "mobile.zip");
        _service.RestoreCurrentDeviceArchive(cloud, Snapshot(target, SaveUnitType.Zip, true), Device);
        AssertZip(target, Payload);
        cloud = _service.CreateCurrentDeviceArchive(Snapshot(target, SaveUnitType.Zip, false), Device, Work("off"));
        _service.RestoreCurrentDeviceArchive(cloud, Snapshot(target, SaveUnitType.Zip, false), Device);
        AssertZip(target, "savedata/" + Payload);
        cloud = _service.CreateCurrentDeviceArchive(Snapshot(target, SaveUnitType.Zip, true), Device, Work("on"));
        _service.RestoreCurrentDeviceArchive(cloud, Snapshot(target, SaveUnitType.Zip, true), Device);
        AssertZip(target, Payload);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalZipBackup_UsesSameLayout_WithoutModifyingSource(bool contentsOnly)
    {
        var input = MakeZip();
        var original = SHA256.HashData(File.ReadAllBytes(input));
        var backup = await new SaveBackupService().BackupAsync(Snapshot(input, SaveUnitType.Zip, contentsOnly), Work("local"));
        AssertZip(Directory.GetFiles(backup, "*.zip", SearchOption.AllDirectories).Single(), contentsOnly ? Payload : "savedata/" + Payload);
        Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(input)));
    }

    [Fact]
    public void LinkedPcFolderAndAndroidZip_DoNotUseForeignDeviceFallbackOnRestore()
    {
        var input = MakeZip();
        var target = Path.Combine(_root, "pc", "savedata");
        var config = new ManagerConfig
        {
            Devices = new() { ["pc"] = new DeviceDefinition { Id = "pc", Name = "PC", Platform = "windows" },
                ["android"] = new DeviceDefinition { Id = "android", Name = "Android", Platform = "android" } },
            Games = [new GameDefinition { Name = "Test", NextSaveUnitId = 2, SavePaths = [
                new SaveUnitDefinition { Id = 0, UnitType = SaveUnitType.Folder, Paths = new() { ["pc"] = target }, LinkedUnitIds = [1] },
                new SaveUnitDefinition { Id = 1, UnitType = SaveUnitType.Zip, Paths = new() { ["android"] = input }, LinkedUnitIds = [0], ContentsOnly = true }
            ] }]
        };
        var snapshot = new GameLibraryService(new EnvironmentTokenResolver(), new CurrentDeviceService())
            .CreateSnapshot(config, Path.Combine(_root, "config.json"), "PC", "windows");
        var pc = snapshot.Games[0];
        Assert.DoesNotContain(pc.SaveUnits.Single(unit => unit.UnitType == SaveUnitType.Zip).Paths, path => path.DeviceId == "pc");
        Assert.Single(snapshot.Games[0].CurrentDevicePaths);
        var cloud = _service.CreateCurrentDeviceArchive(Snapshot(input, SaveUnitType.Zip, true, 1), Device, Work("linked"));
        _service.RestoreCurrentDeviceArchive(cloud, pc, snapshot.CurrentDevice);
        Assert.Equal("progress", File.ReadAllText(Path.Combine(target, Payload)));
    }

    [Fact]
    public void RawZipWithMultipleRootItems_PreservesAllItemsInEitherMode()
    {
        var folder = MakeFolder();
        File.WriteAllText(Path.Combine(folder, "root.bin"), "root-file");
        var input = Path.Combine(_root, "flat.zip");
        ZipFile.CreateFromDirectory(folder, input);
        foreach (var contents in new[] { false, true })
        {
            var cloud = _service.CreateCurrentDeviceArchive(Snapshot(input, SaveUnitType.Zip, contents), Device, Work("flat-" + contents));
            var target = Path.Combine(_root, "flat-" + contents + ".zip");
            _service.RestoreCurrentDeviceArchive(cloud, Snapshot(target, SaveUnitType.Zip, true), Device);
            using var zip = ZipFile.OpenRead(target);
            Assert.NotNull(zip.GetEntry(Payload));
            Assert.NotNull(zip.GetEntry("root.bin"));
        }
    }

    private string MakeFolder()
    {
        var folder = Work("source/savedata");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(folder, Payload))!);
        File.WriteAllText(Path.Combine(folder, Payload), "progress");
        return folder;
    }

    [Fact]
    public void AmbiguousZipAndFolderUnits_AreRejectedBeforeAnyTargetIsWritten()
    {
        var input = MakeZip();
        var game = Snapshot(input, SaveUnitType.Zip, true);
        game = game with { SaveUnits = [.. game.SaveUnits, new ResolvedSaveUnit(2, SaveUnitType.Folder, false,
            [new ResolvedDevicePath("device", "Test", Path.Combine(_root, "not-created"))])] };
        Assert.Throws<InvalidOperationException>(() => _service.CreateCurrentDeviceArchive(game, Device, Work("ambiguous")));
        var hash = SHA256.HashData(File.ReadAllBytes(input));
        Assert.Throws<InvalidOperationException>(() => _service.RestoreCurrentDeviceArchive(input, game, Device));
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(input)));
        Assert.False(Directory.Exists(Path.Combine(_root, "not-created")));
    }

    [Fact]
    public void ActualFolderNamedContent_IsNotAnAdministrativeWrapper()
    {
        var source = Work("real/content");
        File.WriteAllText(Path.Combine(source, "save.bin"), "progress");
        var cloud = _service.CreateCurrentDeviceArchive(Snapshot(source, SaveUnitType.Folder, false), Device, Work("content-cloud"));
        var target = Work("different-name");
        _service.RestoreCurrentDeviceArchive(cloud, Snapshot(target, SaveUnitType.Folder, false), Device);
        Assert.Equal("progress", File.ReadAllText(Path.Combine(target, "content", "save.bin")));
    }

    [Fact]
    public void EmptyZip_CannotOverwriteExistingArchive()
    {
        var empty = Path.Combine(Work("empty"), "empty.zip");
        using (ZipFile.Open(empty, ZipArchiveMode.Create)) { }
        var target = Path.Combine(_root, "existing.zip");
        File.WriteAllText(target, "keep-existing");
        Assert.Throws<InvalidDataException>(() => _service.RestoreCurrentDeviceArchive(empty, Snapshot(target, SaveUnitType.Zip, true), Device));
        Assert.Equal("keep-existing", File.ReadAllText(target));
        Assert.Throws<InvalidDataException>(() => _service.CreateCurrentDeviceArchive(Snapshot(empty, SaveUnitType.Zip, true), Device, Work("empty-upload")));
    }

    private string MakeZip()
    {
        var folder = MakeFolder();
        var path = Path.Combine(_root, "input.zip");
        ZipFile.CreateFromDirectory(Path.GetDirectoryName(folder)!, path);
        return path;
    }

    private string Work(string name) { var path = Path.Combine(_root, name); Directory.CreateDirectory(path); return path; }

    private static void AssertZip(string path, string expected)
    {
        using var zip = ZipFile.OpenRead(path);
        var file = Assert.Single(zip.Entries, entry => !entry.FullName.EndsWith('/'));
        Assert.Equal(expected, file.FullName);
        using var reader = new StreamReader(file.Open());
        Assert.Equal("progress", reader.ReadToEnd());
    }

    private static GameSnapshot Snapshot(string path, SaveUnitType type, bool contents, int id = 0) => new("Test", true,
        [new ResolvedSaveUnit(id, type, false, [new ResolvedDevicePath("device", "Test", path)], ContentsOnly: contents)],
        [], [new CurrentDevicePathInfo(id, type, path, false)], new CurrentDeviceGamePathInfo(""));

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
