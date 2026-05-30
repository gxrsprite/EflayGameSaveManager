using CommunityToolkit.Maui;
using EflayGameSaveManager.Core.Services;
using EflayGameSaveManager.Maui.Services;
using EflayGameSaveManager.Maui.ViewModels;

namespace EflayGameSaveManager.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit();

        builder.Services.AddSingleton<GameSaveManagerConfigurationService>();
        builder.Services.AddSingleton(sp => new GameLibraryService(
            new EnvironmentTokenResolver(),
            new CurrentDeviceService(),
            new AppRuntimeSettingsService()));
        builder.Services.AddSingleton(sp => new CloudSyncService(
            new S3CompatibleCloudStorageClient(),
            new SaveBackupService(),
            new ArchiveTransferService()));
        builder.Services.AddSingleton<MobileWorkspaceService>();
        builder.Services.AddSingleton<FavoriteGamesService>();
        builder.Services.AddSingleton<StoragePickerService>();
        builder.Services.AddSingleton<MainPageViewModel>();
        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }
}
