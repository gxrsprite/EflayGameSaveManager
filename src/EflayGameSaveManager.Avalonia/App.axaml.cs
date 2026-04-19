using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Markup.Xaml;
using EflayGameSaveManager.Avalonia.ViewModels;
using EflayGameSaveManager.Avalonia.Views;
using EflayGameSaveManager.Core.Services;

namespace EflayGameSaveManager.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        RegisterGlobalErrorHandlers();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppLogger.Info("Avalonia application starting.");
            var configurationService = new GameSaveManagerConfigurationService();
            var currentDeviceService = new CurrentDeviceService();
            var gameLibraryService = new GameLibraryService(
                new EnvironmentTokenResolver(),
                currentDeviceService,
                new AppRuntimeSettingsService());
            var cloudSyncService = new CloudSyncService(
                new S3CompatibleCloudStorageClient(),
                new SaveBackupService(),
                new ArchiveTransferService());
            var viewModel = new MainWindowViewModel(configurationService, gameLibraryService, cloudSyncService);

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void RegisterGlobalErrorHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLogger.Error("Unhandled AppDomain exception.", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Error("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };

        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            AppLogger.Error("Unhandled UI thread exception.", args.Exception);
            args.Handled = false;
        };
    }
}
