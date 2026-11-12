using System.Globalization;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The words after "This session" on Today (round 2, boards B-PC-Idle and B-PC-Session): "3 ready · 60 min" before a session,
/// "1/3 · 75 min" during one (places used of the session's cap, as the Tasks page's Today strip counts them), the total time
/// limit of its open tasks last. A part with nothing to say is left out with its dot.
/// </summary>
public static class TodaySessionHint
{
    public static string Of(bool live, int used, int cap, int ready, int minutes)
    {
        string count = live
            ? $"{used.ToString(CultureInfo.InvariantCulture)}/{cap.ToString(CultureInfo.InvariantCulture)}"
            : ready > 0 ? $"{ready.ToString(CultureInfo.InvariantCulture)} ready" : string.Empty;
        string total = minutes > 0 ? EstimateText.Of(minutes) : string.Empty;
        return string.Join(" · ", new[] { count, total }.Where(part => part.Length > 0));
    }
}
