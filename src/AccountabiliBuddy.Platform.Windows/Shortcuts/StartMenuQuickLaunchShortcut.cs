using System.Runtime.InteropServices;
using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.Platform.Windows.Shortcuts;

/// <summary>
/// "AccountabiliBuddy Quick Session" in the current user's Start menu. It runs the app with an argument that
/// opens the quick-session popup (handing over to the copy that is already running). Windows does not let apps
/// pin to the taskbar themselves; the user can pin this shortcut from the Start menu.
/// </summary>
public sealed class StartMenuQuickLaunchShortcut : IQuickLaunchShortcut
{
    private const string ShortcutName = "AccountabiliBuddy Quick Session.lnk";

    private readonly string _executablePath;
    private readonly string _arguments;
    private readonly string _shortcutPath;

    public StartMenuQuickLaunchShortcut(string executablePath, string arguments)
    {
        _executablePath = executablePath;
        _arguments = arguments;
        _shortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);
    }

    public bool IsInstalled => File.Exists(_shortcutPath);

    public void Install()
    {
        // WScript.Shell is the simplest built-in way to write a .lnk file; no extra dependency needed.
        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is not available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic? shortcut = null;
        try
        {
            shortcut = shell.CreateShortcut(_shortcutPath);
            shortcut.TargetPath = _executablePath;
            shortcut.Arguments = _arguments;
            shortcut.WorkingDirectory = Path.GetDirectoryName(_executablePath);
            shortcut.IconLocation = $"{_executablePath},0";
            shortcut.Description = "Start a focus session in AccountabiliBuddy";
            shortcut.Save();
        }
        finally
        {
            if (shortcut is not null)
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            Marshal.FinalReleaseComObject(shell);
        }
    }

    public void Remove()
    {
        if (File.Exists(_shortcutPath))
        {
            File.Delete(_shortcutPath);
        }
    }
}
