using System.Diagnostics;
using AccountabiliBuddy.Core.Ports;
using Microsoft.Win32;

namespace AccountabiliBuddy.Platform.Windows.Integrations;

/// <summary>
/// Finds Freedom and PluckEye from Windows' list of installed programs (the Uninstall keys under HKLM, 64- and 32-bit,
/// and HKCU), with their usual install folders as a fallback. One pass over the registry, a few milliseconds;
/// no processes are looked at and nothing of theirs is opened.
/// </summary>
public sealed class WindowsOtherBlockerDetector : IOtherBlockerDetector
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UninstallKey32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly KnownBlocker[] KnownBlockers =
    {
        // Freedom (freedom.to). "Freedom" alone is a common word, so the name must be exactly it, or start with it
        // and come from its publisher.
        new("Freedom",
            displayName => string.Equals(displayName, "Freedom", StringComparison.OrdinalIgnoreCase),
            new[] { "Freedom.to", "80 Pct Solutions" },
            new[] { @"%ProgramFiles%\Freedom\Freedom.exe", @"%ProgramFiles(x86)%\Freedom\Freedom.exe", @"%LocalAppData%\Programs\Freedom\Freedom.exe" }),
        // PluckEye (pluckeye.net): the name is distinctive enough on its own.
        new("PluckEye",
            displayName => displayName.Contains("PluckEye", StringComparison.OrdinalIgnoreCase),
            new[] { "PluckEye" },
            new[] { @"%ProgramFiles%\PluckEye\pluckeye.exe", @"%ProgramFiles(x86)%\PluckEye\pluckeye.exe" }),
    };

    public IReadOnlyList<OtherBlocker> Detect()
    {
        IReadOnlyList<InstalledProgram> programs = InstalledPrograms();
        var found = new List<OtherBlocker>();
        foreach (KnownBlocker blocker in KnownBlockers)
        {
            InstalledProgram? program = programs.FirstOrDefault(blocker.Matches);
            if (program is not null)
            {
                found.Add(new OtherBlocker(blocker.Name, program.Version));
                continue;
            }

            string? executable = blocker.Executables.Select(Environment.ExpandEnvironmentVariables).FirstOrDefault(File.Exists);
            if (executable is not null)
            {
                found.Add(new OtherBlocker(blocker.Name, FileVersionInfo.GetVersionInfo(executable).ProductVersion));
            }
        }

        return found;
    }

    private static IReadOnlyList<InstalledProgram> InstalledPrograms()
    {
        var programs = new List<InstalledProgram>();
        ReadUninstallKey(Registry.LocalMachine, UninstallKey, programs);
        ReadUninstallKey(Registry.LocalMachine, UninstallKey32, programs);
        ReadUninstallKey(Registry.CurrentUser, UninstallKey, programs);
        return programs;
    }

    private static void ReadUninstallKey(RegistryKey root, string path, List<InstalledProgram> programs)
    {
        try
        {
            using RegistryKey? uninstall = root.OpenSubKey(path);
            if (uninstall is null)
            {
                return;
            }

            foreach (string name in uninstall.GetSubKeyNames())
            {
                using RegistryKey? entry = uninstall.OpenSubKey(name);
                if (entry?.GetValue("DisplayName") is string displayName && displayName.Trim().Length > 0)
                {
                    programs.Add(new InstalledProgram(
                        displayName.Trim(),
                        entry.GetValue("Publisher") as string,
                        entry.GetValue("DisplayVersion") as string));
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // A key this user can't read is skipped; the install folders are still checked.
        }
    }

    private sealed record InstalledProgram(string DisplayName, string? Publisher, string? Version);

    private sealed record KnownBlocker(string Name, Func<string, bool> NameMatches, string[] Publishers, string[] Executables)
    {
        public bool Matches(InstalledProgram program)
        {
            if (NameMatches(program.DisplayName))
            {
                return true;
            }

            // E.g. "Freedom 2.x" from Freedom's own publisher.
            return program.DisplayName.StartsWith(Name, StringComparison.OrdinalIgnoreCase)
                && program.Publisher is not null
                && Publishers.Any(publisher => program.Publisher.Contains(publisher, StringComparison.OrdinalIgnoreCase));
        }
    }
}
