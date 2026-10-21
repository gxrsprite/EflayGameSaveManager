using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using EflayGameSaveManager.Core.Models;

namespace EflayGameSaveManager.Core.Services.GamePlugins;

/// <summary>
/// Cult of the Lamb (咩咩启示录) save-format adapter.
/// <para>
/// Android port (<c>com.cotl.port</c>): <c>"ZB" + gzip(UTF-8 JSON with BOM)</c>,
/// extensionless entry names (e.g. <c>slot_0</c>).
/// </para>
/// <para>
/// Steam PC: <c>'E' + 16-byte key + 16-byte IV + AES-128-CBC(PKCS7) UTF-8 JSON</c>,
/// entries named <c>*.json</c>. Key/IV are random per file and stored inline,
/// so freshly generated key/IV pairs are accepted by the game.
/// </para>
/// <para>Canonical form: BOM-free UTF-8 JSON with the extensionless entry name.</para>
/// </summary>
public sealed class CultOfTheLambSavePlugin : IGameSaveFormatPlugin
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public bool Matches(GameSnapshot game)
    {
        return IsLambName(game.Name) || IsLambName(game.ChineseName);
    }

    /// <summary>
    /// Decides which encoding the target device needs.
    /// <para>
    /// Order of authority: (1) the platform the device actually reported, (2) legacy
    /// heuristics for configs written before platforms were recorded (path shape, device
    /// name), (3) the installed desktop game build. Paths are only a fallback because they
    /// can be redirected or stored with foreign separators.
    /// </para>
    /// </summary>
    public GameSaveTargetPlatform ResolveTarget(GameSnapshot game, CurrentDeviceContext device)
    {
        var paths = game.SaveUnits
            .SelectMany(unit => unit.Paths)
            .Where(path => string.Equals(path.DeviceId, device.DeviceId, StringComparison.Ordinal))
            .Select(path => path.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();

        // 1) Authoritative: the platform the client reported when it registered the device.
        if (AppPlatform.IsAndroid(device.Platform))
        {
            return GameSaveTargetPlatform.AndroidPort;
        }

        // 2) Legacy configs (no platform recorded): fall back to shape/name heuristics.
        if (!AppPlatform.IsDesktop(device.Platform))
        {
            foreach (var path in paths)
            {
                // Normalize separators: a Windows-side configuration may store an Android
                // path with backslashes, which must still be detected as the mobile port.
                var normalized = path.Replace('\\', '/');
                if (normalized.Contains("/Android/", StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith("/sdcard/", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains("/data/data/", StringComparison.OrdinalIgnoreCase))
                {
                    return GameSaveTargetPlatform.AndroidPort;
                }
            }

            if (LooksLikeAndroidDevice(device))
            {
                return GameSaveTargetPlatform.AndroidPort;
            }
        }

        // 3) Desktop: decide from the installed game build. 1.5.0 (Woolhaven) switched slot
        // saves to MessagePack "*.mp"; 1.4.x and older use AES-encrypted JSON.
        if (TryResolveInstalledVersion(game, device, out var installedVersion))
        {
            return installedVersion >= WoolhavenVersion
                ? GameSaveTargetPlatform.SteamPcMessagePack
                : GameSaveTargetPlatform.SteamPc;
        }

        // Fallback when the executable is unavailable: sniff for MessagePack slots.
        if (paths.Any(HasMessagePackSlots))
        {
            return GameSaveTargetPlatform.SteamPcMessagePack;
        }

        return GameSaveTargetPlatform.SteamPc;
    }

    private static readonly Version WoolhavenVersion = new(1, 5, 0);

    /// <summary>
    /// True when the device itself identifies as the Android port (registered device name,
    /// or a save path outside the desktop filesystem). Used as a fallback so temporary
    /// restore redirections cannot silently switch the target platform to desktop.
    /// </summary>
    public static bool LooksLikeAndroidDevice(CurrentDeviceContext device)
    {
        if (!string.IsNullOrWhiteSpace(device.DeviceName) &&
            device.DeviceName.Contains("android", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads the real game build from the Unity player data file
    /// (<c>&lt;exe&gt;_Data/globalgamemanagers</c>), which carries the bundle version
    /// (e.g. <c>1.5.26.1058</c>) as opposed to the executable's Unity engine version.
    /// </summary>
    public static bool TryResolveInstalledVersion(
        GameSnapshot game,
        CurrentDeviceContext device,
        out Version version)
    {
        version = new Version(0, 0);
        var executables = game.GamePaths
            .Where(path => string.Equals(path.DeviceId, device.DeviceId, StringComparison.Ordinal))
            .Select(path => path.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path));

        foreach (var executable in executables)
        {
            if (TryReadBundledVersion(executable, out version))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads the Unity bundle version stored in globalgamemanagers.</summary>
    public static bool TryReadBundledVersion(string gameExecutablePath, out Version version)
    {
        version = new Version(0, 0);
        try
        {
            var directory = Path.GetDirectoryName(gameExecutablePath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return false;
            }

            var dataFile = Path.Combine(
                directory,
                Path.GetFileNameWithoutExtension(gameExecutablePath) + "_Data",
                "globalgamemanagers");

            if (!File.Exists(dataFile))
            {
                dataFile = Directory
                    .EnumerateDirectories(directory, "*_Data")
                    .Select(candidate => Path.Combine(candidate, "globalgamemanagers"))
                    .FirstOrDefault(File.Exists);
            }

            if (string.IsNullOrWhiteSpace(dataFile) || !File.Exists(dataFile))
            {
                return false;
            }

            using var stream = File.OpenRead(dataFile);
            var length = (int)Math.Min(stream.Length, 65536);
            var buffer = new byte[length];
            var read = stream.Read(buffer, 0, length);
            return TryParseVersion(buffer.AsSpan(0, read), out version);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Extracts the game bundle version from the Unity player header.
    /// Unity stores these as 4-byte little-endian length-prefixed ASCII strings, and the
    /// header also contains shorter strings such as <c>1.0</c>, so the entry with the most
    /// numeric components wins (e.g. <c>1.5.26.1058</c> over <c>1.0</c>), while Unity engine
    /// versions (<c>2022.3.62f2</c>, major ≥ 100) and short forms are ignored.
    /// </summary>
    private static bool TryParseVersion(ReadOnlySpan<byte> buffer, out Version version)
    {
        version = new Version(0, 0);
        Version? best = null;
        var bestComponents = 0;

        for (var index = 0; index + 4 < buffer.Length; index++)
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(buffer[index..]);
            if (length < 5 || length > 64)
            {
                continue;
            }

            var start = index + 4;
            if (start + length > buffer.Length)
            {
                continue;
            }

            var text = ReadVersionText(buffer.Slice(start, length));
            if (text is null
                || !TryBuildVersion(text, out var candidate, out var components)
                || components < bestComponents)
            {
                continue;
            }

            best = candidate;
            bestComponents = components;
        }

        if (best is not null)
        {
            version = best;
            return true;
        }

        // Fallback: plain dotted scan for headers without length prefixes.
        for (var index = 0; index < buffer.Length; index++)
        {
            if (!IsDigit(buffer[index]))
            {
                continue;
            }

            var start = index;
            while (index < buffer.Length && (IsDigit(buffer[index]) || buffer[index] == (byte)'.'))
            {
                index++;
            }

            var text = Encoding.ASCII.GetString(buffer[start..index]).TrimEnd('.');
            if (TryBuildVersion(text, out var candidate, out var components) && components >= 3)
            {
                version = candidate;
                return true;
            }
        }

        return false;
    }

    private static string? ReadVersionText(ReadOnlySpan<byte> payload)
    {
        foreach (var value in payload)
        {
            var isNumeric = value is >= (byte)'0' and <= (byte)'9';
            var isDot = value == (byte)'.';
            var isSuffix = value is >= (byte)'a' and <= (byte)'z' or (>= (byte)'A' and <= (byte)'Z');
            if (!isNumeric && !isDot && !isSuffix)
            {
                return null;
            }
        }

        return Encoding.ASCII.GetString(payload);
    }

    private static bool TryBuildVersion(string text, out Version version, out int components)
    {
        version = new Version(0, 0);
        components = 0;

        // Drop an alpha suffix such as "62f2".
        var numeric = text.AsSpan();
        while (numeric.Length > 0 && !IsVersionChar(numeric[^1]))
        {
            numeric = numeric[..^1];
        }

        var parts = numeric.ToString().TrimEnd('.').Split('.');
        if (parts.Length < 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var major) || major <= 0 || major >= 100)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var minor))
        {
            return false;
        }

        components = parts.Length;
        var build = parts.Length > 2 && int.TryParse(parts[2], out var parsedBuild) ? parsedBuild : 0;
        var revision = parts.Length > 3 && int.TryParse(parts[3], out var parsedRevision) ? parsedRevision : 0;
        version = new Version(major, minor, build, revision);
        return true;
    }

    private static bool IsVersionChar(char value) => value is >= '0' and <= '9' or '.';

    private static bool IsDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';

    private static bool HasMessagePackSlots(string savePath)
    {
        try
        {
            var directory = Directory.Exists(savePath)
                ? savePath
                : Path.GetDirectoryName(savePath);
            return !string.IsNullOrWhiteSpace(directory) &&
                   Directory.Exists(directory) &&
                   Directory.EnumerateFiles(directory, "*.mp").Any();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool TryToCanonical(string entryName, byte[] raw, out string canonicalName, out byte[] canonicalBytes)
    {
        canonicalName = entryName;
        canonicalBytes = raw;

        if (raw.Length == 0)
        {
            return false;
        }

        // Android port: "ZB" + gzip(JSON)
        if (raw.Length > 2 && raw[0] == (byte)'Z' && raw[1] == (byte)'B')
        {
            canonicalBytes = ValidateCompleteJson(StripBom(Gunzip(raw[2..])), entryName);
            canonicalName = StripJsonExtension(entryName);
            return true;
        }

        // Steam PC: 'E' + key(16) + iv(16) + AES-128-CBC(JSON)
        if (raw.Length > 33 && raw[0] == (byte)'E')
        {
            try
            {
                canonicalBytes = StripBom(AesDecrypt(raw[1..17], raw[17..33], raw[33..]));
            }
            catch (CryptographicException)
            {
                return false;
            }

            if (!IsJson(canonicalBytes))
            {
                return false;
            }

            canonicalBytes = ValidateCompleteJson(canonicalBytes, entryName);
            canonicalName = StripJsonExtension(entryName);
            return true;
        }

        // Already canonical (plain JSON, e.g. freshly decrypted or hand-made)
        if (IsJson(raw))
        {
            canonicalBytes = StripBom(raw);
            canonicalName = StripJsonExtension(entryName);
            return true;
        }

        // Unknown file (e.g. Prefs.dict): passthrough.
        return false;
    }

    public byte[] FromCanonical(byte[] canonicalJson, GameSaveTargetPlatform target)
    {
        return target switch
        {
            GameSaveTargetPlatform.AndroidPort => AddZbPrefix(Gzip(AddBom(canonicalJson))),
            GameSaveTargetPlatform.SteamPcMessagePack => throw new NotSupportedException(
                "Cult of the Lamb 1.5.x+ stores slot saves as MessagePack (*.mp). " +
                "JSON slot saves are ignored by the game, so a JSON -> MessagePack rewrite is required " +
                "and is not supported by this plugin yet."),
            _ => AddSteamHeader(AesEncrypt(StripBom(canonicalJson), out var key, out var iv), key, iv),
        };
    }

    public string ToTargetName(string canonicalName, GameSaveTargetPlatform target)
    {
        return target switch
        {
            GameSaveTargetPlatform.AndroidPort => StripJsonExtension(canonicalName),
            _ => canonicalName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? canonicalName
                : canonicalName + ".json",
        };
    }

    private static bool IsLambName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalized = name.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return normalized.Contains("cultofthelamb", StringComparison.Ordinal) ||
               normalized.Contains("咩咩启示录", StringComparison.Ordinal);
    }

    private static string StripJsonExtension(string name)
    {
        return name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? name[..^5]
            : name;
    }

    private static bool IsJson(byte[] raw)
    {
        var probe = StripBom(raw);
        return probe.Length > 0 && probe[0] == (byte)'{';
    }

    /// <summary>
    /// A truncated save (e.g. read while the game was still writing it) still starts with
    /// <c>{</c> but is not closed, and restoring it silently loses fields such as timestamps.
    /// Refuse it instead of archiving a broken save.
    /// </summary>
    private static byte[] ValidateCompleteJson(byte[] json, string entryName)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            _ = document.RootElement.EnumerateObject().Count();
            return json;
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidDataException(
                $"存档文件不完整：'{entryName}' 解密后不是合法的 JSON（{ex.Message}）。" +
                "常见原因：备份时游戏正在写入该文件。请先在游戏内存档并完全退出，再重新备份。", ex);
        }
    }

    private static byte[] StripBom(byte[] raw)
    {
        if (raw.Length >= 3 && raw[0] == Utf8Bom[0] && raw[1] == Utf8Bom[1] && raw[2] == Utf8Bom[2])
        {
            return raw[3..];
        }

        return raw;
    }

    private static byte[] AddBom(byte[] raw)
    {
        var result = new byte[Utf8Bom.Length + raw.Length];
        Buffer.BlockCopy(Utf8Bom, 0, result, 0, Utf8Bom.Length);
        Buffer.BlockCopy(raw, 0, result, Utf8Bom.Length, raw.Length);
        return result;
    }

    private static byte[] Gunzip(byte[] raw)
    {
        using var input = new MemoryStream(raw, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] Gzip(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(raw, 0, raw.Length);
        }

        return output.ToArray();
    }

    private static byte[] AesDecrypt(byte[] key, byte[] iv, byte[] cipher)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;
        return aes.CreateDecryptor().TransformFinalBlock(cipher, 0, cipher.Length);
    }

    private static byte[] AesEncrypt(byte[] plain, out byte[] key, out byte[] iv)
    {
        key = RandomNumberGenerator.GetBytes(16);
        iv = RandomNumberGenerator.GetBytes(16);
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;
        return aes.CreateEncryptor().TransformFinalBlock(plain, 0, plain.Length);
    }

    private static byte[] AddSteamHeader(byte[] cipher, byte[] key, byte[] iv)
    {
        var result = new byte[1 + 16 + 16 + cipher.Length];
        result[0] = (byte)'E';
        Buffer.BlockCopy(key, 0, result, 1, 16);
        Buffer.BlockCopy(iv, 0, result, 17, 16);
        Buffer.BlockCopy(cipher, 0, result, 33, cipher.Length);
        return result;
    }

    private static byte[] AddZbPrefix(byte[] gzip)
    {
        var result = new byte[2 + gzip.Length];
        result[0] = (byte)'Z';
        result[1] = (byte)'B';
        Buffer.BlockCopy(gzip, 0, result, 2, gzip.Length);
        return result;
    }

    /// <summary>Decodes one raw save file for diagnostics/tests without a snapshot.</summary>
    public static byte[] ToCanonicalBytes(string entryName, byte[] raw)
    {
        return new CultOfTheLambSavePlugin().TryToCanonical(entryName, raw, out _, out var canonical)
            ? canonical
            : throw new InvalidOperationException($"Unsupported Cult of the Lamb entry: {entryName}");
    }

    /// <summary>Re-orders a BOM as UTF-8 text for assertions.</summary>
    public static string ToJsonString(byte[] canonicalJson) => Encoding.UTF8.GetString(canonicalJson);
}
