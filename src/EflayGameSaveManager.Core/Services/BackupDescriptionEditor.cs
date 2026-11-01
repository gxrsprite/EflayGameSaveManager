using EflayGameSaveManager.Core.Models;

namespace EflayGameSaveManager.Core.Services;

internal static class BackupDescriptionEditor
{
    public static LegacyGameBackups Update(LegacyGameBackups manifest, string date, string deviceId, string? description)
    {
        if (string.IsNullOrWhiteSpace(date))
            throw new InvalidOperationException("No backup is selected.");

        // Legacy backups can have an empty device ID. Match it exactly as well;
        // never fall back to another device's backup with the same timestamp.
        var matches = manifest.Backups.Where(item =>
            string.Equals(item.Date, date, StringComparison.Ordinal) &&
            string.Equals(item.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(matches.Length == 0
                ? $"Backup not found: {date} ({deviceId}). Refresh the backup list."
                : $"Backup identity is ambiguous: {date} ({deviceId}).");

        var selected = matches[0];
        return manifest with
        {
            Backups = manifest.Backups.Select(item => ReferenceEquals(item, selected)
                ? item with { Describe = description?.Trim() ?? string.Empty }
                : item).ToArray()
        };
    }
}
