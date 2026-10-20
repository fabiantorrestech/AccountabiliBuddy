using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

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
    private const string ExeSuffix = ".exe";
    private const int MaxPathLength = 32_767;

    // The cache is dropped once it grows past this size (closed apps' entries are never asked for again).
    private const int MaxCacheEntries = 256;

    // Keyed by id and start time: Windows reuses process ids, and a new app must not get a closed app's name.
    private readonly Dictionary<(uint ProcessId, long StartedAt), string> _nameByProcess = new();

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
        using SafeFileHandle process = PInvoke.OpenProcess_SafeHandle(
            PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        long startedAt = process.IsInvalid ? 0 : StartTimeOf(process);
        if (_nameByProcess.TryGetValue((processId, startedAt), out string? cached))
        {
            return cached;
        }

        if (_nameByProcess.Count >= MaxCacheEntries)
        {
            _nameByProcess.Clear();
        }

        string name = (process.IsInvalid ? null : ImageNameOf(process)) ?? ReadProcessName(processId);
        _nameByProcess[(processId, startedAt)] = name;
        return name;
    }

    /// <summary>When the process started (FILETIME ticks), or 0 when Windows doesn't say.</summary>
    private static long StartTimeOf(SafeFileHandle process)
    {
        if (!PInvoke.GetProcessTimes(process, out var created, out _, out _, out _))
        {
            return 0;
        }

        return ((long)created.dwHighDateTime << 32) | (uint)created.dwLowDateTime;
    }

    /// <summary>The program file's name without ".exe" (like <see cref="Process.ProcessName"/>), or null.</summary>
    private static string? ImageNameOf(SafeFileHandle process)
    {
        Span<char> buffer = new char[MaxPathLength];
        uint length = (uint)buffer.Length;
        if (!PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, ref length) || length == 0)
        {
            return null;
        }

        string fileName = Path.GetFileName(buffer[..(int)length].ToString());
        return fileName.EndsWith(ExeSuffix, StringComparison.OrdinalIgnoreCase) ? fileName[..^ExeSuffix.Length] : fileName;
    }

    /// <summary>The slower way, for the few processes that can't be opened (e.g. some system processes).</summary>
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
