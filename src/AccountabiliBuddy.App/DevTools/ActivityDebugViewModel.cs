using System.Collections.ObjectModel;
using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.DevTools;

/// <summary>
/// Backs the M2 developer view: current window per monitor plus a change log.
/// Must be updated on the UI thread.
/// </summary>
public sealed class ActivityDebugViewModel
{
    private const int MaxLogEntries = 200;

    public ObservableCollection<MonitorActivityRow> Monitors { get; } = new();

    public ObservableCollection<string> Log { get; } = new();

    public void Apply(ActivitySnapshot snapshot)
    {
        MonitorActivityRow row = MonitorActivityRow.From(snapshot);

        int index = IndexOfMonitor(snapshot.MonitorId);
        if (index >= 0)
        {
            Monitors[index] = row;
        }
        else
        {
            Monitors.Add(row);
        }

        Log.Insert(0, row.ToLogLine());
        if (Log.Count > MaxLogEntries)
        {
            Log.RemoveAt(Log.Count - 1);
        }
    }

    private int IndexOfMonitor(string monitorId)
    {
        for (int i = 0; i < Monitors.Count; i++)
        {
            if (Monitors[i].MonitorId == monitorId)
            {
                return i;
            }
        }

        return -1;
    }
}
