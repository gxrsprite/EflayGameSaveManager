using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using EflayGameSaveManager.Core.Models;
using EflayGameSaveManager.Core.Services;
using EflayGameSaveManager.Core.Services.GamePlugins;

namespace EflayGameSaveManager.Core.Tests;

public sealed class CultOfTheLambPluginTests
{
    private static readonly byte[] SampleJson = Encoding.UTF8.GetBytes("{\"slot\":0,\"hp\":3}");

    private static byte[] MakePhoneBytes()
    {
        var payload = new byte[][] { [0xEF, 0xBB, 0xBF], SampleJson }.SelectMany(b => b).ToArray();
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(payload, 0, payload.Length);
        }

        var gz = output.ToArray();
        var result = new byte[2 + gz.Length];
        result[0] = (byte)'Z';
        result[1] = (byte)'B';
        Buffer.BlockCopy(gz, 0, result, 2, gz.Length);
        return result;
    }

    private static GameSnapshot LambSnapshot(string savePath) =>
        new(
            "咩咩启示录",
            true,
            [
                new ResolvedSaveUnit(
                    0,
                    SaveUnitType.Folder,
                    false,
                    [new ResolvedDevicePath("dev-1", "PC", savePath)])
            ],
            [],
            [new CurrentDevicePathInfo(0, SaveUnitType.Folder, savePath, false)],
            new CurrentDeviceGamePathInfo(string.Empty),
            "咩咩启示录");

    [Fact]
    public void Matches_LambNames()
    {
        var plugin = new CultOfTheLambSavePlugin();
        Assert.True(plugin.Matches(LambSnapshot("C:\\saves")));
        Assert.False(plugin.Matches(LambSnapshot("C:\\saves") with { Name = "Hollow Knight", ChineseName = string.Empty }));
    }

    [Fact]
    public void PhoneBytes_NormalizeToCanonical()
    {
        var plugin = new CultOfTheLambSavePlugin();
        Assert.True(plugin.TryToCanonical("slot_0", MakePhoneBytes(), out var name, out var canonical));
        Assert.Equal("slot_0", name);
        Assert.Equal(SampleJson, canonical);
    }

    [Fact]
    public void Canonical_EncodesToSteam_AndBack()
    {
        var plugin = new CultOfTheLambSavePlugin();
        var steam = plugin.FromCanonical(SampleJson, GameSaveTargetPlatform.SteamPc);
        Assert.Equal((byte)'E', steam[0]);
        Assert.Equal("slot_0.json", plugin.ToTargetName("slot_0", GameSaveTargetPlatform.SteamPc));

        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = steam[1..17];
        aes.IV = steam[17..33];
        var plain = aes.CreateDecryptor().TransformFinalBlock(steam[33..], 0, steam.Length - 33);
        Assert.Equal(SampleJson, plain);

        Assert.True(plugin.TryToCanonical("slot_0.json", steam, out var name, out var canonical));
        Assert.Equal("slot_0", name);
        Assert.Equal(SampleJson, canonical);
    }

    [Fact]
    public void Canonical_EncodesToPort_AndBack()
    {
        var plugin = new CultOfTheLambSavePlugin();
        var port = plugin.FromCanonical(SampleJson, GameSaveTargetPlatform.AndroidPort);
        Assert.Equal((byte)'Z', port[0]);
        Assert.Equal((byte)'B', port[1]);
        Assert.Equal("slot_0", plugin.ToTargetName("slot_0.json", GameSaveTargetPlatform.AndroidPort));

        Assert.True(plugin.TryToCanonical("slot_0", port, out _, out var canonical));
        Assert.Equal(SampleJson, canonical);
    }

    [Fact]
    public void TruncatedSave_IsRejectedInsteadOfArchived()
    {
        // A save read mid-write still starts with '{' but never closes, and restoring it
        // silently drops fields (e.g. timestamps). Backup must fail loudly instead.
        var truncated = Encoding.UTF8.GetBytes("{\"AllowSaving\":true,\"Followers_Dissented");
        var payload = new byte[][] { [0xEF, 0xBB, 0xBF], truncated }.SelectMany(b => b).ToArray();

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(payload, 0, payload.Length);
        }

        var gz = output.ToArray();
        var zb = new byte[2 + gz.Length];
        zb[0] = (byte)'Z';
        zb[1] = (byte)'B';
        Buffer.BlockCopy(gz, 0, zb, 2, gz.Length);

        var plugin = new CultOfTheLambSavePlugin();
        var ex = Assert.Throws<InvalidDataException>(() =>
            plugin.TryToCanonical("slot_0", zb, out _, out _));
        Assert.Contains("存档文件不完整", ex.Message);
    }

    [Fact]
    public void UnknownFile_PassesThrough()
    {
        var plugin = new CultOfTheLambSavePlugin();
        var raw = Encoding.UTF8.GetBytes("unlockedAchievements=2;");
        Assert.False(plugin.TryToCanonical("Prefs.dict", raw, out _, out _));
    }

    [Fact]
    public void ResolveTarget_SniffsAndroidPaths()
    {
        var plugin = new CultOfTheLambSavePlugin();
        var device = new CurrentDeviceContext("dev-1", "PC", false);
        Assert.Equal(
            GameSaveTargetPlatform.AndroidPort,
            plugin.ResolveTarget(LambSnapshot("/storage/emulated/0/Android/data/com.cotl.port/files/user"), device));
        Assert.Equal(
            GameSaveTargetPlatform.SteamPc,
            plugin.ResolveTarget(LambSnapshot("C:\\Users\\e\\saves"), device));
    }

    [Fact]
    public void ResolveTarget_DetectsMessagePackEraFromMpFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cotl-mp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "slot_0.mp"), [0xD3]);
            var plugin = new CultOfTheLambSavePlugin();
            var device = new CurrentDeviceContext("dev-1", "PC", false);
            Assert.Equal(
                GameSaveTargetPlatform.SteamPcMessagePack,
                plugin.ResolveTarget(LambSnapshot(dir), device));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveTarget_PrefersReportedPlatformOverPathShape()
    {
        // Platform is explicit data: a redirected cache path must not flip an Android
        // device to the desktop format, and a desktop device keeps the desktop format
        // even if a foreign-looking path is configured.
        var plugin = new CultOfTheLambSavePlugin();
        var cachePath = @"C:\Temp\cache\shizuku-restore\unit-0";

        var android = new CurrentDeviceContext("dev-android", "phone", false, AppPlatform.Android);
        Assert.Equal(GameSaveTargetPlatform.AndroidPort, plugin.ResolveTarget(LambSnapshot(cachePath), android));

        var desktop = new CurrentDeviceContext("dev-pc", "EFLAYPC", false, AppPlatform.Windows);
        Assert.NotEqual(GameSaveTargetPlatform.AndroidPort, plugin.ResolveTarget(LambSnapshot(@"C:\saves"), desktop));
    }

    [Fact]
    public void AppPlatform_NormalizesFrontendValues()
    {
        Assert.Equal(AppPlatform.Android, AppPlatform.Normalize("Android"));
        Assert.Equal(AppPlatform.Windows, AppPlatform.Normalize("WinUI"));
        Assert.Equal(AppPlatform.MacOs, AppPlatform.Normalize("MacCatalyst"));
        Assert.True(AppPlatform.IsAndroid("ANDROID"));
        Assert.True(AppPlatform.IsDesktop("windows"));
        Assert.False(AppPlatform.IsDesktop("android"));
    }

    [Fact]
    public void EnsureCurrentDevice_RecordsPlatform()
    {
        var config = new ManagerConfig();
        var service = new CurrentDeviceService();

        var created = service.EnsureCurrentDevice(config, "TestPhone", AppPlatform.Android);
        Assert.Equal(AppPlatform.Android, created.Platform);
        Assert.Equal(AppPlatform.Android, config.Devices[created.DeviceId].Platform);

        // A later run reporting a different platform refreshes the stored value.
        var refreshed = service.EnsureCurrentDevice(config, "TestPhone", AppPlatform.Windows);
        Assert.Equal(AppPlatform.Windows, refreshed.Platform);
        Assert.Equal(AppPlatform.Windows, config.Devices[refreshed.DeviceId].Platform);
        Assert.Equal(created.DeviceId, refreshed.DeviceId);
    }

    [Fact]
    public void ResolveTarget_UsesDeviceWhenPathIsRedirectedToCache()
    {
        // The Android restore redirects the unit path into an app cache folder before
        // Core runs; the device identity must keep the target on the mobile format.
        var plugin = new CultOfTheLambSavePlugin();
        var android = new CurrentDeviceContext("dev-android", "Android", false);
        var cachePath = @"C:\Users\eflay\AppData\Local\Temp\cache\shizuku-restore\unit-0";
        Assert.Equal(GameSaveTargetPlatform.AndroidPort, plugin.ResolveTarget(LambSnapshot(cachePath), android));
        Assert.True(CultOfTheLambSavePlugin.LooksLikeAndroidDevice(android));
        Assert.False(CultOfTheLambSavePlugin.LooksLikeAndroidDevice(new CurrentDeviceContext("dev-pc", "EFLAYPC", false)));
    }

    [Fact]
    public void EncodeStaging_HonorsTargetOverride()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cotl-override-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "slot_0"), SampleJson);
            var plugin = new CultOfTheLambSavePlugin();
            var pcDevice = new CurrentDeviceContext("dev-pc", "EFLAYPC", false);

            GameSaveFormatPlugins.EncodeStaging(
                plugin, dir, LambSnapshot(@"C:\saves"), pcDevice,
                GameSaveTargetPlatform.AndroidPort);

            var raw = File.ReadAllBytes(Path.Combine(dir, "slot_0"));
            Assert.Equal((byte)'Z', raw[0]);
            Assert.Equal((byte)'B', raw[1]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ResolveTarget_DetectsAndroidPathWithBackslashes()
    {
        // A Windows-side config may store the phone path with backslashes; it must still be
        // recognized as the mobile port, otherwise restores write PC-format .json files.
        var plugin = new CultOfTheLambSavePlugin();
        var device = new CurrentDeviceContext("dev-1", "Android", false);
        var windowsStyle = @"C:\Temp\sim\Android\data\com.cotl.port\files\user";
        Assert.Equal(GameSaveTargetPlatform.AndroidPort, plugin.ResolveTarget(LambSnapshot(windowsStyle), device));
    }

    [Fact]
    public void MessagePackTarget_RefusesJsonRewrite()
    {
        var plugin = new CultOfTheLambSavePlugin();
        Assert.Throws<NotSupportedException>(() =>
            plugin.FromCanonical(SampleJson, GameSaveTargetPlatform.SteamPcMessagePack));
    }

    [Theory]
    [InlineData("1.5.26.1058", true)]
    [InlineData("1.4.6.0", false)]
    [InlineData("1.4.14.0", false)]
    public void InstalledBuild_DecidesSaveEra(string bundleVersion, bool expectMessagePack)
    {
        var root = Path.Combine(Path.GetTempPath(), "cotl-ver-" + Guid.NewGuid().ToString("N"));
        var dataDir = Path.Combine(root, "Cult Of The Lamb_Data");
        Directory.CreateDirectory(dataDir);
        var exe = Path.Combine(root, "Cult Of The Lamb.exe");
        File.WriteAllBytes(exe, []);

        // Emulate the Unity header: engine version first, then the game bundle version.
        var payload = new List<byte>();
        payload.AddRange(System.Text.Encoding.ASCII.GetBytes("2022.3.62f2"));
        payload.AddRange(new byte[64]);
        payload.AddRange(System.Text.Encoding.ASCII.GetBytes(bundleVersion));
        File.WriteAllBytes(Path.Combine(dataDir, "globalgamemanagers"), payload.ToArray());

        try
        {
            var plugin = new CultOfTheLambSavePlugin();
            var device = new CurrentDeviceContext("dev-1", "PC", false);
            var snapshot = LambSnapshot("C:\\nothing-here") with
            {
                GamePaths = [new ResolvedGamePath("dev-1", "PC", exe)]
            };

            var target = plugin.ResolveTarget(snapshot, device);
            Assert.Equal(
                expectMessagePack ? GameSaveTargetPlatform.SteamPcMessagePack : GameSaveTargetPlatform.SteamPc,
                target);

            Assert.True(CultOfTheLambSavePlugin.TryReadBundledVersion(exe, out var parsed));
            Assert.Equal(bundleVersion, parsed.ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LengthPrefixedHeader_PrefersFullBuildOverShortForms()
    {
        // Mirrors a real Unity header: engine version, then "1.0", "1.0", "1.5.26.1058"
        // each as a 4-byte little-endian length-prefixed ASCII string.
        var root = Path.Combine(Path.GetTempPath(), "cotl-ver-" + Guid.NewGuid().ToString("N"));
        var dataDir = Path.Combine(root, "Cult Of The Lamb_Data");
        Directory.CreateDirectory(dataDir);
        var exe = Path.Combine(root, "Cult Of The Lamb.exe");
        File.WriteAllBytes(exe, []);

        var payload = new List<byte>();
        payload.AddRange(System.Text.Encoding.ASCII.GetBytes("2022.3.62f2"));
        payload.AddRange(new byte[32]);
        foreach (var text in new[] { "1.0", "1.0", "1.5.26.1058" })
        {
            payload.AddRange(BitConverter.GetBytes(text.Length));
            payload.AddRange(System.Text.Encoding.ASCII.GetBytes(text));
            payload.Add(0);
        }

        File.WriteAllBytes(Path.Combine(dataDir, "globalgamemanagers"), payload.ToArray());

        try
        {
            Assert.True(CultOfTheLambSavePlugin.TryReadBundledVersion(exe, out var parsed));
            Assert.Equal("1.5.26.1058", parsed.ToString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EngineVersionAlone_IsNotTakenAsGameVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "cotl-ver-" + Guid.NewGuid().ToString("N"));
        var dataDir = Path.Combine(root, "Game_Data");
        Directory.CreateDirectory(dataDir);
        var exe = Path.Combine(root, "Game.exe");
        File.WriteAllBytes(exe, []);
        File.WriteAllBytes(
            Path.Combine(dataDir, "globalgamemanagers"),
            System.Text.Encoding.ASCII.GetBytes("2022.3.62f2"));
        try
        {
            Assert.False(CultOfTheLambSavePlugin.TryReadBundledVersion(exe, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
