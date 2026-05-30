namespace EflayGameSaveManager.Core.Models;

public sealed record LocalGameBackup(
    string GameName,
    string Date,
    string Describe,
    string Path,
    long Size,
    string? Parent,
    string DeviceId,
    bool IsCurrentDeviceHead,
    bool IsDeviceHead);