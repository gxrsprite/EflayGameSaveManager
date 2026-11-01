using EflayGameSaveManager.Core.Models;

namespace EflayGameSaveManager.Maui.ViewModels;

public sealed record CloudBackupChoice(CloudGameBackup Backup, string DeviceName)
{
    public string Label => $"{Backup.Date} | {DeviceName} | {Size}";
    public string Size => Backup.Size < 1024 ? $"{Backup.Size} B" : Backup.Size < 1024 * 1024 ? $"{Backup.Size / 1024d:0.0} KB" : $"{Backup.Size / 1024d / 1024d:0.0} MB";
    public string Details => $"{DeviceName} · {Size}" + (Backup.IsCurrentDeviceHead ? " · 本设备最新" : "");
    public string Description => string.IsNullOrWhiteSpace(Backup.Describe) ? "暂无描述" : $"描述：{Backup.Describe}";
}
