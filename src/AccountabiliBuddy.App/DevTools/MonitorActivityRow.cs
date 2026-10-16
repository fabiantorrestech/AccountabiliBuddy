using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.DevTools;

/// <summary>Display-ready view of one <see cref="ActivitySnapshot"/>.</summary>
public sealed record MonitorActivityRow(string MonitorId, string ProcessName, string WindowTitle, string Time)
{
    private const string EmptyDesktopLabel = "(desktop)";

    public static MonitorActivityRow From(ActivitySnapshot snapshot)
    {
        string processName = snapshot.IsEmptyDesktop ? EmptyDesktopLabel : snapshot.ProcessName;
        string time = snapshot.Timestamp.ToLocalTime().ToString("HH:mm:ss");
        return new MonitorActivityRow(snapshot.MonitorId, processName, snapshot.WindowTitle, time);
    }

    public string ToLogLine()
    {
        return $"{Time}  {MonitorId,-14} {ProcessName,-22} {WindowTitle}";
    }
}
