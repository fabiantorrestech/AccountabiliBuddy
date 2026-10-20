using System.ComponentModel;
using System.Diagnostics;
using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.Platform.Windows.Display;

/// <summary>
/// Finds a running app's program file by process name, shows files in File Explorer (selected), and opens sites in
/// the default browser. Apps the current user can't inspect (e.g. running as administrator) give no program file.
/// </summary>
public sealed class Win32DesktopShell : IDesktopShell
{
    public string? FindRunningProgram(string processName)
    {
        return RunningProgramFile(processName);
    }

    public void ShowInFileManager(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        // explorer.exe /select,"<file>" opens its folder with the file selected.
        using Process? _ = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
    }

    public void OpenWebsite(string domain)
    {
        if (Uri.CheckHostName(domain) != UriHostNameType.Dns || !Uri.TryCreate($"https://{domain}/", UriKind.Absolute, out Uri? address))
        {
            return;
        }

        using Process? _ = Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
    }

    /// <summary>The program file of the first running process with this name that can be inspected, or null.</summary>
    internal static string? RunningProgramFile(string processName)
    {
        Process[] processes = Process.GetProcessesByName(processName);
        try
        {
            foreach (Process process in processes)
            {
                try
                {
                    if (process.MainModule?.FileName is string path)
                    {
                        return path;
                    }
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // Elevated or already exited; try the next one.
                }
            }

            return null;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }
}
