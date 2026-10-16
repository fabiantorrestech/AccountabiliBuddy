using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace AccountabiliBuddy.Platform.Windows.Activity;

/// <summary>
/// Resolves the process name (without ".exe") that owns a window.
/// Store/UWP apps (Settings, Calculator, ...) are hosted by ApplicationFrameHost; for those
/// the real app process is found through the hosted child window.
/// Not thread-safe.
/// </summary>
internal sealed class ProcessNameResolver
{
    private const string UwpHostProcessName = "ApplicationFrameHost";

    // Process ids get reused eventually, so the cache is dropped once it grows past this size.
    private const int MaxCacheEntries = 256;

    private readonly Dictionary<uint, string> _nameByProcessId = new();

    public string Resolve(HWND window, uint processId)
    {
        string name = GetProcessName(processId);
        if (!string.Equals(name, UwpHostProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        uint hostedProcessId = FindHostedProcessId(window, processId);
        return hostedProcessId == 0 ? name : GetProcessName(hostedProcessId);
    }

    private string GetProcessName(uint processId)
    {
        if (_nameByProcessId.TryGetValue(processId, out string? cached))
        {
            return cached;
        }

        if (_nameByProcessId.Count >= MaxCacheEntries)
        {
            _nameByProcessId.Clear();
        }

        string name = ReadProcessName(processId);
        _nameByProcessId[processId] = name;
        return name;
    }

    private static string ReadProcessName(uint processId)
    {
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            // Process already exited.
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static uint FindHostedProcessId(HWND hostWindow, uint hostProcessId)
    {
        uint hostedProcessId = 0;

        PInvoke.EnumChildWindows(hostWindow, (child, _) =>
        {
            PInvoke.GetWindowThreadProcessId(child, out uint childProcessId);
            if (childProcessId != hostProcessId)
            {
                hostedProcessId = childProcessId;
                return false; // stop enumerating
            }

            return true;
        }, default);

        return hostedProcessId;
    }
}
