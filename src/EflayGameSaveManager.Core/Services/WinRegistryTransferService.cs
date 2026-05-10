using System.Diagnostics;

namespace EflayGameSaveManager.Core.Services;

internal sealed class WinRegistryTransferService
{
    public bool KeyExists(string registryPath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(registryPath))
        {
            return false;
        }

        return RunReg(["query", registryPath]).ExitCode == 0;
    }

    public bool ExportKey(string registryPath, string destinationFile)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(registryPath))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        if (File.Exists(destinationFile))
        {
            File.Delete(destinationFile);
        }

        var result = RunReg(["export", registryPath, destinationFile, "/y"]);
        if (result.ExitCode != 0)
        {
            AppLogger.Error($"Failed to export registry key: {registryPath}. reg.exe exit {result.ExitCode}. {result.Output}");
            return false;
        }

        return File.Exists(destinationFile);
    }

    public bool ImportFile(string registryFile)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(registryFile))
        {
            return false;
        }

        var result = RunReg(["import", registryFile]);
        if (result.ExitCode != 0)
        {
            AppLogger.Error($"Failed to import registry file: {registryFile}. reg.exe exit {result.ExitCode}. {result.Output}");
            return false;
        }

        return true;
    }

    public bool DeleteKey(string registryPath)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(registryPath) || !KeyExists(registryPath))
        {
            return false;
        }

        var result = RunReg(["delete", registryPath, "/f"]);
        if (result.ExitCode != 0)
        {
            AppLogger.Error($"Failed to delete registry key: {registryPath}. reg.exe exit {result.ExitCode}. {result.Output}");
            return false;
        }

        return true;
    }

    private static (int ExitCode, string Output) RunReg(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "reg.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start reg.exe.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, string.Join(Environment.NewLine, [output, error]).Trim());
    }
}
