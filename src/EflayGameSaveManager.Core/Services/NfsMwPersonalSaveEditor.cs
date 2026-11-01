using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EflayGameSaveManager.Core.Services;

public sealed record NfsMwPersonalProfile(string SavePath, string ProfileName, string Alias, uint Money, ulong TotalBounty, int CurrentBlacklistStage);

/// <summary>Edits the Profile fields used by the NFS Most Wanted (2005) PC save editor.</summary>
public sealed class NfsMwPersonalSaveEditor
{
    private const int MinimumLength = 0xE865 + 4 + 16;
    private const int FileSizeOffset = 0x04;
    private const int CrcBlock1Offset = 0x10;
    private const int CrcDataOffset = 0x14;
    private const int CrcBlock2Offset = 0x18;
    private const int CrcBlock1Start = 0x1C;
    private const int CrcBlock1End = 0x24;
    private const int CrcDataStart = 0x24;
    private const int SavedDataStart = 0x34;
    private const int MoneyOffset = 0x4039;
    private const int CurrentBlacklistStageOffset = 0x4038;
    private const int ExpectedNfsMwPcSaveLength = 0xF86C;
    private const int GarageRecordsOffset = 0xE2ED;
    private const int GarageRecordSize = 0x38;
    private const int GarageRecordCount = 25;
    private const int SoldHistoryBountyOffset = 0xE865;
    private const int GarageBountyOffset = 0x10;
    private const int AliasOffset = 0x5A31;
    private const int AliasBufferLength = 0x24;
    private const int AliasMaxLength = 16;
    private const int Md5Length = 16;
    private static readonly (int Start, int End)[] BossReadyProgressionSpans =
    [
        (0x0034, 0x4034),
        (0x4038, 0x4039),
        (0x403D, 0x429D),
        (0x42B9, 0x5241),
        (0x5241, 0x5739),
        (0x5B41, 0x5B42),
        (0x5B62, 0x5B64)
    ];

    public NfsMwPersonalProfile Load(string saveDirectory)
    {
        var path = ResolveSaveFile(saveDirectory);
        var bytes = File.ReadAllBytes(path);
        ValidateLayout(bytes);
        return CreateProfile(path, bytes);
    }

    public IReadOnlyList<NfsMwPersonalProfile> ListProfiles(string saveDirectory)
    {
        if (File.Exists(saveDirectory)) return [Load(saveDirectory)];
        if (!Directory.Exists(saveDirectory)) throw new DirectoryNotFoundException($"存档目录不存在：{saveDirectory}");

        return Directory.EnumerateFiles(saveDirectory, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(path).Contains(".bak_", StringComparison.OrdinalIgnoreCase)
                && IsPlausibleSave(path))
            .Select(TryLoadProfile)
            .Where(profile => profile is not null)
            .Select(profile => profile!)
            .OrderBy(profile => profile.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public NfsMwPersonalProfile Save(string saveDirectory, string alias, uint money, ulong totalBounty)
    {
        var path = ResolveSaveFile(saveDirectory);
        var bytes = File.ReadAllBytes(path);
        ValidateLayout(bytes);
        var tail = bytes.AsSpan(bytes.Length - Md5Length, Md5Length);
        var useFullFileHash = CryptographicOperations.FixedTimeEquals(tail, MD5.HashData(bytes.AsSpan(0, bytes.Length - Md5Length)));
        var aliasBytes = Encoding.ASCII.GetBytes(alias ?? string.Empty);
        if (aliasBytes.Length > AliasMaxLength || (alias?.Any(value => value > 0x7f) ?? false))
        {
            throw new InvalidOperationException("档案名只能使用 ASCII 字符，最多 16 个字符。");
        }
        SetTotalBounty(bytes, totalBounty);

        bytes.AsSpan(AliasOffset, AliasBufferLength).Clear();
        aliasBytes.CopyTo(bytes.AsSpan(AliasOffset, AliasBufferLength));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(MoneyOffset, 4), money);
        RepairIntegrity(bytes, useFullFileHash);

        var backup = path + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        File.Copy(path, backup, overwrite: false);
        var temporary = path + ".rgsm_tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        return CreateProfile(path, bytes);
    }

    /// <summary>Applies the reference editor's Boss Ready progress for the save's current Blacklist stage.</summary>
    public NfsMwPersonalProfile ApplyBossFightReady(string saveDirectory)
    {
        var path = ResolveSaveFile(saveDirectory);
        var bytes = File.ReadAllBytes(path);
        ValidateLayout(bytes);
        if (bytes.Length != ExpectedNfsMwPcSaveLength)
            throw new InvalidDataException("Boss Ready 挑战进度只支持已验证的 63,596 字节版 NFS Most Wanted PC 存档；文件未修改。");
        if (!bytes.AsSpan(0x44, 4).SequenceEqual("Game"u8))
            throw new InvalidDataException("存档缺少 NFS MW 游戏区段标识；文件未修改。");

        var stage = bytes[CurrentBlacklistStageOffset];
        if (stage is < 1 or > 15)
            throw new InvalidDataException($"当前黑名单阶段值 {stage} 无效（应为 1 到 15）；文件未修改。");

        var template = LoadBossReadyTemplate(stage);
        var previousTotalBounty = ReadTotalBounty(bytes);
        var tail = bytes.AsSpan(bytes.Length - Md5Length, Md5Length);
        var useFullFileHash = CryptographicOperations.FixedTimeEquals(tail, MD5.HashData(bytes.AsSpan(0, bytes.Length - Md5Length)));

        foreach (var span in template.Spans)
            span.Bytes.CopyTo(bytes.AsSpan(span.Start, span.Bytes.Length));

        // The template's total is the stage's Blacklist requirement. Keep all earned bounty and add only the missing amount.
        if (previousTotalBounty < template.MinimumTotalBounty)
            SetTotalBounty(bytes, template.MinimumTotalBounty);

        RepairIntegrity(bytes, useFullFileHash);
        WriteSaveWithBackup(path, bytes);
        return CreateProfile(path, bytes);
    }

    private static string ResolveSaveFile(string directory)
    {
        if (File.Exists(directory)) return directory;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"存档目录不存在：{directory}");

        var candidates = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(path).Contains(".bak_", StringComparison.OrdinalIgnoreCase))
            .Where(path => IsPlausibleSave(path))
            .ToArray();
        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidDataException("当前存档目录中没有找到可识别的 NFS Most Wanted PC 存档文件。"),
            _ => throw new InvalidDataException("当前存档目录中找到多个 NFS Most Wanted 存档文件，请将存档单独放入一个目录后再编辑。")
        };
    }

    private static bool IsPlausibleSave(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length < MinimumLength || info.Length > int.MaxValue) return false;
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[8];
            if (stream.Read(header) != header.Length) return false;
            return BinaryPrimitives.ReadUInt32LittleEndian(header[FileSizeOffset..]) == info.Length;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static NfsMwPersonalProfile? TryLoadProfile(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            ValidateLayout(bytes);
            return CreateProfile(path, bytes);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return null;
        }
    }

    private static NfsMwPersonalProfile CreateProfile(string path, byte[] bytes) =>
        new(path, Path.GetFileName(Path.GetDirectoryName(path)) ?? Path.GetFileName(path),
            ReadAlias(bytes), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(MoneyOffset, 4)), ReadTotalBounty(bytes), bytes[CurrentBlacklistStageOffset]);

    private static BossReadyTemplate LoadBossReadyTemplate(int stage)
    {
        var resourceName = $"EflayGameSaveManager.Core.Assets.NfsMwBossReady.stage{stage:00}.json";
        using var stream = typeof(NfsMwPersonalSaveEditor).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"找不到黑名单阶段 {stage} 的 Boss Ready 模板。");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (root.GetProperty("stage").GetInt32() != stage)
            throw new InvalidDataException($"Boss Ready 模板阶段与当前阶段 {stage} 不一致。");

        var minimumTotalBounty = root.GetProperty("minimum_total_bounty").GetUInt32();
        var spans = root.GetProperty("spans").EnumerateArray().ToArray();
        if (spans.Length != BossReadyProgressionSpans.Length)
            throw new InvalidDataException("Boss Ready 模板进度区段数量无效。");

        var decodedSpans = new (int Start, byte[] Bytes)[spans.Length];
        for (var index = 0; index < spans.Length; index++)
        {
            var start = spans[index].GetProperty("start").GetInt32();
            var end = spans[index].GetProperty("end").GetInt32();
            var expected = BossReadyProgressionSpans[index];
            if (start != expected.Start || end != expected.End || end <= start || end > ExpectedNfsMwPcSaveLength)
                throw new InvalidDataException($"Boss Ready 模板第 {index + 1} 个进度区段边界无效。");

            var data = Convert.FromHexString(spans[index].GetProperty("hex").GetString() ?? string.Empty);
            if (data.Length != end - start)
                throw new InvalidDataException($"Boss Ready 模板第 {index + 1} 个进度区段数据长度无效。");
            decodedSpans[index] = (start, data);
        }

        var gameSection = decodedSpans[0].Bytes;
        if (!gameSection.AsSpan(16, 4).SequenceEqual("Game"u8) ||
            !CryptographicOperations.FixedTimeEquals(gameSection.AsSpan(0, 16), MD5.HashData(gameSection.AsSpan(16))) ||
            decodedSpans[1].Bytes[0] != stage)
            throw new InvalidDataException("Boss Ready 模板游戏区段校验失败；文件未修改。");

        return new BossReadyTemplate(minimumTotalBounty, decodedSpans);
    }

    private static void WriteSaveWithBackup(string path, byte[] bytes)
    {
        var backup = path + ".bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        File.Copy(path, backup, overwrite: false);
        var temporary = path + ".rgsm_tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed record BossReadyTemplate(uint MinimumTotalBounty, (int Start, byte[] Bytes)[] Spans);

    private static ulong ReadTotalBounty(byte[] bytes)
    {
        ulong liveBounty = 0;
        for (var slot = 0; slot < GarageRecordCount; slot++)
        {
            var offset = GarageRecordsOffset + slot * GarageRecordSize;
            if (bytes[offset] == 0xFF) continue;
            if (bytes[offset] != slot || bytes[offset + 1] != 0xCD || bytes[offset + 7] != 0 ||
                bytes[offset + 8] != 0 || bytes[offset + 9] != 0 || bytes[offset + 10] != 0xCD || bytes[offset + 11] != 0xCD)
                throw new InvalidDataException($"车库记录 {slot} 的占用标记或格式异常，已停止修改以保护存档。");
            liveBounty += BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + GarageBountyOffset, 4));
        }
        return liveBounty + BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(SoldHistoryBountyOffset, 4));
    }

    private static void SetTotalBounty(byte[] bytes, ulong target)
    {
        ulong liveBounty = 0;
        for (var slot = 0; slot < GarageRecordCount; slot++)
        {
            var offset = GarageRecordsOffset + slot * GarageRecordSize;
            if (bytes[offset] == 0xFF) continue;
            if (bytes[offset] != slot || bytes[offset + 1] != 0xCD || bytes[offset + 7] != 0 ||
                bytes[offset + 8] != 0 || bytes[offset + 9] != 0 || bytes[offset + 10] != 0xCD || bytes[offset + 11] != 0xCD)
                throw new InvalidDataException($"车库记录 {slot} 的占用标记或格式异常，已停止修改以保护存档。");
            liveBounty += BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + GarageBountyOffset, 4));
        }
        if (target < liveBounty)
            throw new InvalidOperationException($"个人总赏金不能低于当前在用车库记录的赏金合计 {liveBounty:N0}。此编辑只调整已售车辆历史赏金，不会改动车库车辆赏金。");
        var soldBounty = target - liveBounty;
        if (soldBounty > uint.MaxValue)
            throw new InvalidOperationException($"当前车库赏金合计为 {liveBounty:N0}，已售车辆历史字段最多容纳 {uint.MaxValue:N0}。");
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(SoldHistoryBountyOffset, 4), (uint)soldBounty);
    }

    private static void ValidateLayout(byte[] bytes)
    {
        if (bytes.Length < MinimumLength || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(FileSizeOffset, 4)) != bytes.Length)
            throw new InvalidDataException("文件长度或 NFS MW 存档头无效，未修改存档。");
        if (AliasOffset + AliasBufferLength > bytes.Length - Md5Length)
            throw new InvalidDataException("存档长度不足，未修改存档。");
    }

    private static string ReadAlias(byte[] bytes)
    {
        var field = bytes.AsSpan(AliasOffset, AliasBufferLength);
        var zero = field.IndexOf((byte)0);
        if (zero >= 0) field = field[..zero];
        if (field.IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            throw new InvalidDataException("存档档案名不是 ASCII 字符，已停止编辑以保护原存档。");
        return Encoding.ASCII.GetString(field);
    }

    private static void RepairIntegrity(byte[] bytes, bool useFullFileHash)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(FileSizeOffset, 4), (uint)bytes.Length);
        var tail = bytes.AsSpan(bytes.Length - Md5Length, Md5Length);
        var savedDataDigest = MD5.HashData(bytes.AsSpan(SavedDataStart, bytes.Length - SavedDataStart - Md5Length));
        var fullDigest = MD5.HashData(bytes.AsSpan(0, bytes.Length - Md5Length));
        var digest = useFullFileHash ? fullDigest : savedDataDigest;
        digest.CopyTo(tail);

        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(CrcBlock1Offset, 4), EaCrc32(bytes.AsSpan(CrcBlock1Start, CrcBlock1End - CrcBlock1Start)));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(CrcDataOffset, 4), EaCrc32(bytes.AsSpan(CrcDataStart)));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(CrcBlock2Offset, 4), EaCrc32(bytes.AsSpan(0, CrcBlock2Offset)));
    }

    private static uint EaCrc32(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4) return 0;
        uint crc = BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
        crc = ~crc;
        foreach (var value in data[4..])
        {
            var index = (byte)(crc >> 24);
            crc = ((crc << 8) | value) ^ CrcTable[index];
        }
        return ~crc;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var crc = index << 24;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04C11DB7 : crc << 1;
            table[index] = crc;
        }
        return table;
    }
}
