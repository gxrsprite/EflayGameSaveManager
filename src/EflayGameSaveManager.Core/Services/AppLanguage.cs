namespace EflayGameSaveManager.Core.Services;

public static class AppLanguage
{
    public const string Chinese = "zh-CN";
    public const string English = "en-US";

    public static string Resolve(string? configuredLanguage, string? machineName)
    {
        if (string.Equals(configuredLanguage, English, StringComparison.OrdinalIgnoreCase))
        {
            return English;
        }

        if (string.Equals(configuredLanguage, Chinese, StringComparison.OrdinalIgnoreCase))
        {
            return Chinese;
        }

        return IsEnglishDefaultMachine(machineName) ? English : Chinese;
    }

    private static bool IsEnglishDefaultMachine(string? machineName)
    {
        return string.Equals(machineName, "LOCALHOST", StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(machineName) &&
                machineName.Contains("GameSir", StringComparison.OrdinalIgnoreCase));
    }
}
