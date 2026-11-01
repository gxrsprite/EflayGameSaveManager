using Android.App;
using Android.Content;
using AndroidX.Activity.Result;
using AndroidX.Activity.Result.Contract;

namespace EflayGameSaveManager.Maui.Platforms.Android;

/// <summary>SAF selection with persistent read/write access to the actual document.</summary>
public static class ZipDocumentAccess
{
    private static ActivityResultLauncher? _launcher;
    private static TaskCompletionSource<string?>? _pending;

    public static void Register(MainActivity activity) =>
        _launcher = activity.RegisterForActivityResult(new ActivityResultContracts.StartActivityForResult(), new Callback());

    public static async Task<string?> PickAsync()
    {
        if (_launcher is null) throw new InvalidOperationException("文件选择器尚未准备就绪。");
        if (_pending is not null) throw new InvalidOperationException("已有文件选择窗口打开。");
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = completion;
        try
        {
            var intent = new Intent(Intent.ActionOpenDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType("*/*");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantPersistableUriPermission);
            _launcher.Launch(intent);
            return await completion.Task;
        }
        finally { _pending = null; }
    }

    private sealed class Callback : Java.Lang.Object, IActivityResultCallback
    {
        public void OnActivityResult(Java.Lang.Object? result)
        {
            var completion = _pending;
            if (completion is null) return;
            try
            {
                if (result is not ActivityResult activityResult || activityResult.ResultCode != (int)Result.Ok || activityResult.Data?.Data is not { } uri)
                {
                    completion.TrySetResult(null);
                    return;
                }
                var resolver = global::Android.App.Application.Context.ContentResolver!;
                using var cursor = resolver.Query(uri, [global::Android.Provider.IOpenableColumns.DisplayName], null, null, null);
                var name = cursor?.MoveToFirst() == true ? cursor.GetString(0) : null;
                if (!string.Equals(Path.GetExtension(name), ".zip", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("请选择 .zip 存档文件。");
                var flags = activityResult.Data.Flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
                if (!flags.HasFlag(ActivityFlags.GrantWriteUriPermission))
                    throw new InvalidOperationException("所选文件不支持写回，请选择本地可读写的 ZIP 文件。");
                resolver.TakePersistableUriPermission(uri, flags);
                completion.TrySetResult(uri.ToString());
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        }
    }
}
