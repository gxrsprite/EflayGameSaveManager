using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using EflayGameSaveManager.Maui.Platforms.Android;

namespace EflayGameSaveManager.Maui;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        RequestStoragePermission();
        RequestShizukuPermission();
    }

    private void RequestShizukuPermission()
    {
        try
        {
            ShizukuInterop.RequestPermission(0);
        }
        catch
        {
            // Shizuku not available
        }
    }

    private void RequestStoragePermission()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            if (!Android.OS.Environment.IsExternalStorageManager)
            {
                var intent = new Intent(Settings.ActionManageAllFilesAccessPermission);
                StartActivity(intent);
            }
        }
    }
}
