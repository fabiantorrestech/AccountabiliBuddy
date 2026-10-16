using Windows.Win32;

namespace AccountabiliBuddy.Platform.Windows.Shortcuts;

/// <summary>
/// Windows only lets the app the user just interacted with bring a window to the front. A second launch
/// (e.g. the quick-session shortcut) calls this before signalling the running copy, so that copy may
/// show its popup in front instead of only flashing in the taskbar.
/// </summary>
public static class ForegroundHandOff
{
    public static void AllowAnyProcess()
    {
        PInvoke.AllowSetForegroundWindow(PInvoke.ASFW_ANY);
    }
}
