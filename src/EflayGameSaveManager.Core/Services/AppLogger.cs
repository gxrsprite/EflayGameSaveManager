using System.Text;

namespace EflayGameSaveManager.Core.Services;

public static class AppLogger
{
    private static readonly Lock SyncRoot = new();

    static AppLogger()
    {
        var baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EflayGameSaveManager",
            "logs");
        Directory.CreateDirectory(baseDirectory);
        LogFilePath = Path.Combine(baseDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");
    }

    public static string LogFilePath { get; }

    public static void Info(string message)
    {
        Write("INFO", message, null);
    }

    public static void Error(string message, Exception? exception = null)
    {
        Write("ERROR", message, exception);
    }

    private static void Write(string level, string message, Exception? exception)
    {
        var builder = new StringBuilder();
        builder.Append('[')
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
            .Append("] [")
            .Append(level)
            .Append("] ")
            .AppendLine(message);

        if (exception is not null)
        {
            builder.AppendLine(exception.ToString());
        }

        lock (SyncRoot)
        {
            File.AppendAllText(LogFilePath, builder.ToString(), Encoding.UTF8);
        }
    }
}
