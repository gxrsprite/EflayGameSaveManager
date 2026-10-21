namespace EflayGameSaveManager.Core.Services;

/// <summary>
/// Wildcard filtering for save units.
/// <para>
/// Patterns are matched case-insensitively against a file's name and its
/// forward-slash relative path. Supported wildcards: <c>*</c> (any run, including
/// separators) and <c>?</c> (one character). A trailing <c>/</c> pattern excludes a
/// whole sub-directory.
/// </para>
/// <para>Examples: <c>settings*</c>, <c>*.log</c>, <c>Prefs.dict</c>, <c>cache/</c>.</para>
/// </summary>
public static class SavePathFilter
{
    /// <summary>Splits a UI text field (semicolons, commas or newlines) into patterns.</summary>
    public static List<string> ParsePatterns(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Renders patterns back into the single-line UI format.</summary>
    public static string FormatPatterns(IEnumerable<string>? patterns)
    {
        return patterns is null ? string.Empty : string.Join("; ", patterns);
    }

    public static bool IsExcluded(string fileName, string? relativePath, IReadOnlyList<string>? patterns)
    {
        if (patterns is null || patterns.Count == 0)
        {
            return false;
        }

        var normalizedRelative = string.IsNullOrWhiteSpace(relativePath)
            ? fileName
            : relativePath.Replace('\\', '/').TrimStart('/');

        foreach (var rawPattern in patterns)
        {
            var pattern = rawPattern?.Trim();
            if (string.IsNullOrWhiteSpace(pattern) || pattern.StartsWith('#'))
            {
                continue;
            }

            pattern = pattern.Replace('\\', '/');

            // Directory pattern: "cache/" or "cache/*" excludes everything beneath it.
            if (pattern.EndsWith('/'))
            {
                var directory = pattern.TrimEnd('/');
                if (normalizedRelative.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase) ||
                    IsWildcardMatch(normalizedRelative, directory + "/*"))
                {
                    return true;
                }

                continue;
            }

            if (IsWildcardMatch(fileName, pattern) || IsWildcardMatch(normalizedRelative, pattern))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Case-insensitive glob match supporting <c>*</c> and <c>?</c>.</summary>
    public static bool IsWildcardMatch(string input, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return string.IsNullOrEmpty(input);
        }

        // Iterative wildcard match with linear backtracking.
        var inputIndex = 0;
        var patternIndex = 0;
        var starIndex = -1;
        var matchIndex = 0;

        while (inputIndex < input.Length)
        {
            if (patternIndex < pattern.Length &&
                (pattern[patternIndex] == '?' ||
                 char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(input[inputIndex])))
            {
                inputIndex++;
                patternIndex++;
                continue;
            }

            if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex;
                matchIndex = inputIndex;
                patternIndex++;
                continue;
            }

            if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                matchIndex++;
                inputIndex = matchIndex;
                continue;
            }

            return false;
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    /// <summary>
    /// Copies <paramref name="sourceDirectory"/> into <paramref name="destinationDirectory"/>,
    /// skipping files that match <paramref name="patterns"/>. Excluded directories are not traversed.
    /// </summary>
    public static void CopyDirectory(
        string sourceDirectory,
        string destinationDirectory,
        IReadOnlyList<string>? patterns,
        Action<string, string> copyFile)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.GetFiles(sourceDirectory))
        {
            var name = Path.GetFileName(file);
            if (IsExcluded(name, name, patterns))
            {
                continue;
            }

            copyFile(file, Path.Combine(destinationDirectory, name));
        }

        foreach (var directory in Directory.GetDirectories(sourceDirectory))
        {
            var name = Path.GetFileName(directory);
            if (IsExcluded(name, name + "/", patterns))
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(destinationDirectory, name), patterns, copyFile);
        }
    }

    /// <summary>
    /// Deletes the contents of a directory except entries matching the exclusion patterns,
    /// so a "delete before apply" restore never removes locally-owned files.
    /// </summary>
    public static void DeleteExcluding(string directory, IReadOnlyList<string>? patterns)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        if (patterns is null || patterns.Count == 0)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }

        foreach (var file in Directory.GetFiles(directory))
        {
            var name = Path.GetFileName(file);
            if (!IsExcluded(name, name, patterns))
            {
                FileCopyHelper.DeleteFileIfExists(file);
            }
        }

        foreach (var child in Directory.GetDirectories(directory))
        {
            var name = Path.GetFileName(child);
            if (IsExcluded(name, name + "/", patterns))
            {
                continue;
            }

            DeleteExcluding(child, patterns);
            if (!Directory.EnumerateFileSystemEntries(child).Any())
            {
                Directory.Delete(child);
            }
        }
    }
}
