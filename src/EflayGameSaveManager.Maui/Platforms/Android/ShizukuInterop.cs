using Android.Runtime;
using JavaClass = Java.Lang.Class;
using JavaObject = Java.Lang.Object;

namespace EflayGameSaveManager.Maui.Platforms.Android;

/// <summary>
/// Shizuku bridge using managed Java peers so the Android runtime owns JNI references.
/// </summary>
public static class ShizukuInterop
{
    private static JavaClass GetShizukuClass() => JavaClass.ForName(
        "rikka.shizuku.Shizuku", true, global::Android.App.Application.Context.ClassLoader)
        ?? throw new InvalidOperationException("Shizuku API unavailable.");

    private static bool ReadBoolean(string name)
    {
        try
        {
            using var type = GetShizukuClass();
            using var method = type.GetDeclaredMethod(name);
            using var value = method?.Invoke(null);
            return value?.JavaCast<global::Java.Lang.Boolean>()?.BooleanValue() == true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsAvailable => ReadBoolean("pingBinder");

    public static bool HasPermission => ReadInt("checkSelfPermission", -1) == 0;

    public static int Uid => ReadInt("getUid", -1);

    public static bool IsRoot => Uid == 0;

    private static int ReadInt(string name, int fallback)
    {
        try
        {
            using var type = GetShizukuClass();
            using var method = type.GetDeclaredMethod(name);
            using var value = method?.Invoke(null);
            return value?.JavaCast<global::Java.Lang.Integer>()?.IntValue() ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static void RequestPermission(int code)
    {
        try
        {
            if (!IsAvailable || ReadBoolean("isPreV11") || HasPermission ||
                ReadBoolean("shouldShowRequestPermissionRationale")) return;

            using var type = GetShizukuClass();
            using var method = type.GetDeclaredMethod("requestPermission", global::Java.Lang.Integer.Type!);
            using var requestCode = global::Java.Lang.Integer.ValueOf(code);
            using var result = method?.Invoke(null, requestCode);
        }
        catch
        {
            // Shizuku may have stopped or permission may have been revoked.
        }
    }

    /// <summary>
    /// Runs a shell command and converts Java/API failures into an ordinary error result.
    /// Call from a worker thread because process I/O blocks.
    /// </summary>
    public static (int ExitCode, string Stdout, string Stderr) RunShellCommand(string command)
    {
        try
        {
            using var type = GetShizukuClass();
            using var stringArrayType = JavaClass.ForName("[Ljava.lang.String;");
            using var stringType = JavaClass.FromType(typeof(global::Java.Lang.String));
            using var method = type.GetDeclaredMethod("newProcess", stringArrayType!, stringArrayType!, stringType);
            if (method is null)
                return (-1, "", "Shizuku newProcess unavailable.");

            // newProcess is private in Shizuku v13. Accessible belongs to the reflected
            // Method, not to Shizuku itself. No raw JNI handles cross worker threads.
            method.Accessible = true;
            using var shell = new global::Java.Lang.String("sh");
            using var option = new global::Java.Lang.String("-c");
            using var script = new global::Java.Lang.String(command);
            using var arguments = global::Java.Lang.Reflect.Array.NewInstance(stringType, 3)!;
            global::Java.Lang.Reflect.Array.Set(arguments, 0, shell);
            global::Java.Lang.Reflect.Array.Set(arguments, 1, option);
            global::Java.Lang.Reflect.Array.Set(arguments, 2, script);
            using var result = method.Invoke(null, new JavaObject[] { arguments, null!, null! });
            if (result is null)
                return (-1, "", "Shizuku newProcess returned null.");

            using var process = result.JavaCast<global::Java.Lang.Process>();
            try
            {
                using var stdoutReader = new StreamReader(process.InputStream!);
                using var stderrReader = new StreamReader(process.ErrorStream!);
                // Drain both pipes concurrently: a full stderr pipe must not block
                // the process while we wait for stdout to close.
                var stdout = stdoutReader.ReadToEndAsync();
                var stderr = stderrReader.ReadToEndAsync();
                var exitCode = process.WaitFor();
                Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
                return (exitCode, stdout.Result, stderr.Result);
            }
            finally
            {
                process.Destroy();
            }
        }
        catch (Exception ex)
        {
            return (-1, "", "Shizuku error: " + ex.Message);
        }
    }
}
