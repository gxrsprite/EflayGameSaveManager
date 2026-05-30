using Android.Runtime;
using Java.Interop;

namespace EflayGameSaveManager.Maui.Platforms.Android;

/// <summary>
/// JNI-based interop with the Shizuku Java API (rikka.shizuku.Shizuku).
/// Uses reflection to call private newProcess() for privileged shell commands.
/// </summary>
public static class ShizukuInterop
{
    private static IntPtr _shizukuClass;
    private static IntPtr _newProcessMethod;
    private static bool _initialized;

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            _shizukuClass = JNIEnv.FindClass("rikka/shizuku/Shizuku");
            _newProcessMethod = JNIEnv.GetStaticMethodID(_shizukuClass, "newProcess",
                "([Ljava/lang/String;[Ljava/lang/String;Ljava/lang/String;)Lrikka/shizuku/ShizukuRemoteProcess;");
            // Make accessible (it's private in v13+)
            JNIEnv.CallStaticVoidMethod(_shizukuClass,
                JNIEnv.GetStaticMethodID(_shizukuClass, "setAccessible",
                    "(Ljava/lang/reflect/AccessibleObject;Z)V"));
        }
        catch
        {
            // Shizuku classes not available
        }
    }

    public static bool IsAvailable
    {
        get
        {
            try
            {
                EnsureInitialized();
                if (_shizukuClass == IntPtr.Zero) return false;
                var method = JNIEnv.GetStaticMethodID(_shizukuClass, "pingBinder", "()Z");
                return JNIEnv.CallStaticBooleanMethod(_shizukuClass, method);
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool HasPermission
    {
        get
        {
            try
            {
                EnsureInitialized();
                if (_shizukuClass == IntPtr.Zero) return false;
                var method = JNIEnv.GetStaticMethodID(_shizukuClass, "checkSelfPermission", "()I");
                return JNIEnv.CallStaticIntMethod(_shizukuClass, method) == 0; // PERMISSION_GRANTED
            }
            catch
            {
                return false;
            }
        }
    }

    public static int Uid
    {
        get
        {
            try
            {
                EnsureInitialized();
                var method = JNIEnv.GetStaticMethodID(_shizukuClass, "getUid", "()I");
                return JNIEnv.CallStaticIntMethod(_shizukuClass, method);
            }
            catch
            {
                return -1;
            }
        }
    }

    public static bool IsRoot => Uid == 0;

    public static void RequestPermission(int code)
    {
        try
        {
            EnsureInitialized();
            if (IsPreV11 || HasPermission) return;
            if (ShouldShowRationale) return;
            var method = JNIEnv.GetStaticMethodID(_shizukuClass, "requestPermission", "(I)V");
            JNIEnv.CallStaticVoidMethod(_shizukuClass, method, new JValue(code));
        }
        catch
        {
            // Shizuku not available
        }
    }

    private static bool IsPreV11
    {
        get
        {
            try
            {
                var method = JNIEnv.GetStaticMethodID(_shizukuClass, "isPreV11", "()Z");
                return JNIEnv.CallStaticBooleanMethod(_shizukuClass, method);
            }
            catch { return true; }
        }
    }

    private static bool ShouldShowRationale
    {
        get
        {
            try
            {
                var method = JNIEnv.GetStaticMethodID(_shizukuClass, "shouldShowRequestPermissionRationale", "()Z");
                return JNIEnv.CallStaticBooleanMethod(_shizukuClass, method);
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Runs a shell command via Shizuku's private newProcess() method.
    /// Returns (exitCode, stdout, stderr).
    /// </summary>
    public static (int ExitCode, string Stdout, string Stderr) RunShellCommand(string command)
    {
        EnsureInitialized();

        // Call private method: Shizuku.newProcess(new String[]{"sh","-c",cmd}, null, null)
        var cmdArray = JNIEnv.NewArray(new[] {
            new global::Java.Lang.String("sh"),
            new global::Java.Lang.String("-c"),
            new global::Java.Lang.String(command)
        });
        var result = JNIEnv.CallStaticObjectMethod(_shizukuClass, _newProcessMethod,
            new JValue(cmdArray), new JValue(IntPtr.Zero), new JValue(IntPtr.Zero));

        if (result == IntPtr.Zero)
            return (-1, "", "Shizuku newProcess returned null");

        // Read stdout/stderr from the ShizukuRemoteProcess
        var procClass = JNIEnv.GetObjectClass(result);

        var getInputStream = JNIEnv.GetMethodID(procClass, "getInputStream", "()Ljava/io/InputStream;");
        var getErrorStream = JNIEnv.GetMethodID(procClass, "getErrorStream", "()Ljava/io/InputStream;");
        var waitFor = JNIEnv.GetMethodID(procClass, "waitFor", "()I");
        var destroy = JNIEnv.GetMethodID(procClass, "destroy", "()V");

        var stdoutStream = JNIEnv.CallObjectMethod(result, getInputStream);
        var stderrStream = JNIEnv.CallObjectMethod(result, getErrorStream);

        var stdout = ReadStream(stdoutStream);
        var stderr = ReadStream(stderrStream);

        var exitCode = JNIEnv.CallIntMethod(result, waitFor);
        JNIEnv.CallVoidMethod(result, destroy);

        JNIEnv.DeleteLocalRef(cmdArray);
        JNIEnv.DeleteLocalRef(result);
        JNIEnv.DeleteLocalRef(procClass);

        return (exitCode, stdout, stderr);
    }

    private static string ReadStream(IntPtr inputStream)
    {
        if (inputStream == IntPtr.Zero) return "";

        // Use Java Scanner to read the entire stream as a string
        var scannerClass = JNIEnv.FindClass("java/util/Scanner");
        var scannerCtor = JNIEnv.GetMethodID(scannerClass, "<init>", "(Ljava/io/InputStream;Ljava/lang/String;)V");
        var useDelimiter = JNIEnv.GetMethodID(scannerClass, "useDelimiter", "(Ljava/lang/String;)Ljava/util/Scanner;");
        var hasNext = JNIEnv.GetMethodID(scannerClass, "hasNext", "()Z");
        var next = JNIEnv.GetMethodID(scannerClass, "next", "()Ljava/lang/String;");
        var close = JNIEnv.GetMethodID(scannerClass, "close", "()V");

        var charset = JNIEnv.NewString("UTF-8");
        var delimiter = JNIEnv.NewString("\\A");
        var scanner = JNIEnv.NewObject(scannerClass, scannerCtor, new JValue(inputStream), new JValue(charset));
        JNIEnv.CallObjectMethod(scanner, useDelimiter, new JValue(delimiter));

        var result = "";
        if (JNIEnv.CallBooleanMethod(scanner, hasNext))
        {
            var str = JNIEnv.CallObjectMethod(scanner, next);
            result = JNIEnv.GetString(str, JniHandleOwnership.DoNotTransfer) ?? "";
        }

        JNIEnv.CallVoidMethod(scanner, close);
        JNIEnv.DeleteLocalRef(scanner);
        JNIEnv.DeleteLocalRef(scannerClass);
        JNIEnv.DeleteLocalRef(charset);
        JNIEnv.DeleteLocalRef(delimiter);

        return result;
    }
}
