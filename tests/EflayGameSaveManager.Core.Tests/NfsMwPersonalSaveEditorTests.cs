using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using EflayGameSaveManager.Core.Services;

namespace EflayGameSaveManager.Core.Tests;

public sealed class NfsMwPersonalSaveEditorTests
{
    [Fact]
    public void BossReady_AllStagesPreservePersonalDataAndCreateExactBackup()
    {
        using var fixture = new Fixture();
        var editor = new NfsMwPersonalSaveEditor();
        for (var stage = 1; stage <= 15; stage++)
        {
            var path = fixture.CreateSave(stage, 20_000_000);
            var original = File.ReadAllBytes(path);
            var result = editor.ApplyBossFightReady(path);
            var actual = File.ReadAllBytes(path);

            Assert.Equal(stage, result.CurrentBlacklistStage);
            Assert.Equal("PLAYER", result.Alias);
            Assert.Equal(123456u, result.Money);
            Assert.Equal(20_005_000ul, result.TotalBounty);
            Assert.Equal(original[0x8000], actual[0x8000]);
            Assert.Equal(original.AsSpan(0xE2ED, 0x38).ToArray(), actual.AsSpan(0xE2ED, 0x38).ToArray());
            Assert.Equal(original, File.ReadAllBytes(Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".bak_*"))));
            Assert.Equal(MD5.HashData(actual.AsSpan(0x44, 0x4034 - 0x44)), actual.AsSpan(0x34, 16).ToArray());
        }
    }

    [Fact]
    public void BossReady_CompensatesLowTotalBountyWithoutChangingCarBounty()
    {
        using var fixture = new Fixture();
        var path = fixture.CreateSave(15, 0);
        var editor = new NfsMwPersonalSaveEditor();
        var result = editor.ApplyBossFightReady(path);
        var bytes = File.ReadAllBytes(path);
        Assert.True(result.TotalBounty >= 20_000);
        Assert.Equal(5_000u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0xE2ED + 0x10, 4)));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(16, true)]
    [InlineData(15, false)]
    public void BossReady_InvalidStageOrMagicDoesNotWrite(int stage, bool validMagic)
    {
        using var fixture = new Fixture();
        var path = fixture.CreateSave(stage, 0, validMagic);
        var before = File.ReadAllBytes(path);
        Assert.Throws<InvalidDataException>(() => new NfsMwPersonalSaveEditor().ApplyBossFightReady(path));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.bak_*"));
    }

    [Fact]
    public void Save_RejectsNonAsciiAliasBeforeWriting()
    {
        using var fixture = new Fixture();
        var path = fixture.CreateSave(15, 0);
        var before = File.ReadAllBytes(path);
        Assert.Throws<InvalidOperationException>(() => new NfsMwPersonalSaveEditor().Save(path, "中文", 10, 5_000));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "rgsm-nfs-tests-" + Guid.NewGuid().ToString("N"));

        public string CreateSave(int stage, uint soldBounty, bool validMagic = true)
        {
            var directory = Path.Combine(_root, stage + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "PLAYER");
            var data = new byte[0xF86C];
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4, 4), (uint)data.Length);
            Encoding.ASCII.GetBytes(validMagic ? "Game" : "BAD!").CopyTo(data, 0x44);
            data[0x4038] = (byte)stage;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x4039, 4), 123456);
            Encoding.ASCII.GetBytes("PLAYER").CopyTo(data, 0x5A31);
            data[0x8000] = 0xA5;
            for (var slot = 0; slot < 25; slot++) data[0xE2ED + slot * 0x38] = 0xFF;
            data[0xE2ED] = 0;
            data[0xE2ED + 1] = data[0xE2ED + 10] = data[0xE2ED + 11] = 0xCD;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0xE2ED + 0x10, 4), 5000);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0xE865, 4), soldBounty);
            File.WriteAllBytes(path, data);
            return path;
        }

        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
