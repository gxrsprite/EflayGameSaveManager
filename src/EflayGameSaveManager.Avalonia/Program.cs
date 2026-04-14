using System;
using Avalonia;
using EflayGameSaveManager.Core.Services;

namespace EflayGameSaveManager.Avalonia;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            AppLogger.Info("Program.Main entered.");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Fatal startup exception.", ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
