using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.Platform.Windows.Power;

/// <summary>
/// Reads the System event log for why the PC went down (ADR 60): 1074 names who shut it down, 6008 and 41 mean it went down
/// unexpectedly. Local only, and only these events; anything that can't be read reads as unknown.
/// </summary>
public sealed class EventLogShutdownCauseReader : IShutdownCauseReader
{
    private const int EvtQueryChannelPath = 0x1;
    private const int EvtQueryReverseDirection = 0x200;
    private const int EvtRenderEventXml = 1;
    private const int ReadTimeoutMs = 1000;

    // The programs Windows Update restarts a PC through. Process names, so they don't depend on the display language.
    private static readonly string[] UpdatePrograms = ["trustedinstaller", "mousocoreworker", "usoclient", "musnotif", "wuauclt"];

    public ShutdownCause Read(DateTimeOffset since)
    {
        string time = since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        string query = $"*[System[(EventID=1074 or EventID=6008 or EventID=41) and TimeCreated[@SystemTime>='{time}']]]";
        IntPtr results = IntPtr.Zero;
        IntPtr handle = IntPtr.Zero;
        try
        {
            results = EvtQuery(IntPtr.Zero, "System", query, EvtQueryChannelPath | EvtQueryReverseDirection);
            if (results == IntPtr.Zero || !EvtNext(results, 1, out handle, ReadTimeoutMs, 0, out int returned) || returned == 0)
            {
                return ShutdownCause.Unknown;
            }

            return CauseOf(Render(handle));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or System.Xml.XmlException)
        {
            return ShutdownCause.Unknown;
        }
        finally
        {
            Close(handle);
            Close(results);
        }
    }

    private static ShutdownCause CauseOf(string? xml)
    {
        if (xml is null)
        {
            return ShutdownCause.Unknown;
        }

        XDocument document = XDocument.Parse(xml);
        string? id = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "EventID")?.Value;
        if (id != "1074")
        {
            return ShutdownCause.Unexpected;
        }

        string? program = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Data")?.Value;
        return program is not null && UpdatePrograms.Any(name => program.Contains(name, StringComparison.OrdinalIgnoreCase))
            ? ShutdownCause.WindowsUpdate
            : ShutdownCause.ShutDown;
    }

    private static string? Render(IntPtr handle)
    {
        EvtRender(IntPtr.Zero, handle, EvtRenderEventXml, 0, IntPtr.Zero, out int needed, out _);
        if (needed <= 0)
        {
            return null;
        }

        IntPtr buffer = Marshal.AllocHGlobal(needed);
        try
        {
            return EvtRender(IntPtr.Zero, handle, EvtRenderEventXml, needed, buffer, out _, out _)
                ? Marshal.PtrToStringUni(buffer)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void Close(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            EvtClose(handle);
        }
    }

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr EvtQuery(IntPtr session, string path, string query, int flags);

    [DllImport("wevtapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EvtNext(IntPtr results, int count, out IntPtr events, int timeout, int flags, out int returned);

    [DllImport("wevtapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EvtRender(IntPtr context, IntPtr fragment, int flags, int bufferSize, IntPtr buffer, out int bufferUsed, out int propertyCount);

    [DllImport("wevtapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EvtClose(IntPtr handle);
}
