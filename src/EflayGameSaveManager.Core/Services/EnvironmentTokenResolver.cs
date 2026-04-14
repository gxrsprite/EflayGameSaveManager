namespace EflayGameSaveManager.Core.Services;

public sealed class EnvironmentTokenResolver
{
    private readonly IReadOnlyDictionary<string, string> _tokenMap;

    public EnvironmentTokenResolver()
    {
        _tokenMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["<home>"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ["<winDocuments>"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ["<winAppData>"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ["<winLocalAppData>"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ["<winCommonAppData>"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ["<winCommonDocuments>"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
            ["<winPublic>"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
            ["<winDesktop>"] = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            ["<winLocalAppDataLow>"] = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData",
                "LocalLow")
        };
    }

    public string ResolvePath(string path)
    {
        var resolved = path;

        foreach (var pair in _tokenMap)
        {
            resolved = resolved.Replace(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return Environment.ExpandEnvironmentVariables(resolved);
    }
}
