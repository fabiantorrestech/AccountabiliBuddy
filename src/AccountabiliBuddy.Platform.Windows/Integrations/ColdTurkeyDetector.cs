using System.Diagnostics;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Platform.Windows.Browser;
using Microsoft.Win32;

namespace AccountabiliBuddy.Platform.Windows.Integrations;

/// <summary>
/// Detects Cold Turkey Blocker from public signs only: its install folder and file version, its running
/// processes, and its native messaging host registrations. Its settings database is never opened
/// (the format is private and obfuscated).
/// </summary>
public sealed class ColdTurkeyDetector : IColdTurkeyDetector
{
    private const string MainExecutable = "Cold Turkey Blocker.exe";
    private const string HostName = "com.coldturkey.coldturkey";

    // Its UI and its helper/service processes; any of them running means Cold Turkey is active.
    private static readonly string[] ProcessNames = { "Cold Turkey Blocker", "ServiceHub.Helper", "CTMsgHostChrome", "CTMsgHostEdge", "CTMsgHostFirefox" };

    public ColdTurkeyStatus Detect()
    {
        string? folder = FindInstallFolder();
        if (folder is null)
        {
            return ColdTurkeyStatus.NotInstalled;
        }

        string? version = FileVersionInfo.GetVersionInfo(Path.Combine(folder, MainExecutable)).ProductVersion;
        bool running = ProcessNames.Any(IsRunningFrom(folder));

        var browsersWithHost = BrowserCatalog.All
            .Where(browser => HasHost(browser.NativeHostKey))
            .Select(browser => browser.Kind)
            .ToList();

        return new ColdTurkeyStatus(true, folder, version, running, browsersWithHost);
    }

    private static string? FindInstallFolder()
    {
        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Cold Turkey"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Cold Turkey"),
        };

        return candidates.FirstOrDefault(folder => File.Exists(Path.Combine(folder, MainExecutable)));
    }

    private static Func<string, bool> IsRunningFrom(string folder)
    {
        return processName =>
        {
            Process[] processes = Process.GetProcessesByName(processName);
            try
            {
                // ServiceHub.Helper is a generic name; only count it when it lives in Cold Turkey's folder.
                return processes.Any(process => IsInFolder(process, folder));
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        };
    }

    private static bool IsInFolder(Process process, string folder)
    {
        try
        {
            string? path = process.MainModule?.FileName;
            return path is not null && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Elevated/service processes cannot be inspected from a normal process; the name alone has to do.
            return !string.Equals(process.ProcessName, "ServiceHub.Helper", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool HasHost(string nativeHostKey)
    {
        foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using RegistryKey? key = root.OpenSubKey($@"{nativeHostKey}\{HostName}");
            if (key is not null)
            {
                return true;
            }
        }

        return false;
    }
}
