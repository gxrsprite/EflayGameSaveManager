namespace EflayGameSaveManager.Core.Services;

public static class CloudStoragePathHelper
{
    public static string NormalizeRootPath(string rootPath)
    {
        return string.IsNullOrWhiteSpace(rootPath)
            ? string.Empty
            : rootPath.Trim().Trim('/').Replace('\\', '/');
    }

    public static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var buffer = value
            .Select(character => invalid.Contains(character) || character == '/' || character == '\\' || char.IsControl(character)
                ? '-'
                : character)
            .ToArray();

        return string.IsNullOrWhiteSpace(value)
            ? "unnamed"
            : string.Join(string.Empty, buffer).Trim(' ', '.', '-').Replace("  ", " ");
    }

    public static string CombineKey(params string[] segments)
    {
        return string.Join(
            '/',
            segments
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .Select(segment => NormalizeRootPath(segment)));
    }
}
