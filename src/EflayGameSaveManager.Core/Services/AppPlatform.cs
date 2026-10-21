namespace EflayGameSaveManager.Core.Services;

/// <summary>
/// Canonical platform identifiers shared by every frontend.
/// <para>
/// The platform of a device is data, not something to infer from a file path: a path can be
/// temporarily redirected (Android restores write through an app cache folder) or written with
/// foreign separators, which is exactly how the mobile port ended up receiving desktop-format
/// saves. Each app reports its real platform (<c>DeviceInfo.Current.Platform</c>,
/// <c>OperatingSystem.IsWindows()</c>, Android APIs) and Core stores it on the device entry.
/// </para>
/// </summary>
public static class AppPlatform
{
    public const string Android = "android";
    public const string Windows = "windows";
    public const string MacOs = "macos";
    public const string Linux = "linux";
    public const string Unknown = "unknown";

    /// <summary>Platform of the process that is running right now.</summary>
    public static string Current { get; } = Detect();

    private static string Detect()
    {
        if (OperatingSystem.IsAndroid())
        {
            return Android;
        }

        if (OperatingSystem.IsWindows())
        {
            return Windows;
        }

        if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS())
        {
            return MacOs;
        }

        if (OperatingSystem.IsLinux())
        {
            return Linux;
        }

        return Unknown;
    }

    /// <summary>Normalizes frontend values such as "Android", "WinUI" or "windows" to a canonical id.</summary>
    public static string Normalize(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return Unknown;
        }

        var value = platform.Trim().ToLowerInvariant();
        return value switch
        {
            "android" => Android,
            "windows" or "winui" or "win32nt" => Windows,
            "macos" or "maccatalyst" or "ios" or "tvos" => MacOs,
            "linux" or "gtk" or "x11" => Linux,
            _ => value
        };
    }

    public static bool IsAndroid(string? platform) =>
        string.Equals(Normalize(platform), Android, StringComparison.OrdinalIgnoreCase);

    public static bool IsDesktop(string? platform)
    {
        var normalized = Normalize(platform);
        return normalized is Windows or MacOs or Linux;
    }
}
