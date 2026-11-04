using System.Globalization;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The plain words for "who can this PC actually reach": the line on the Devices page while no other device has been
/// heard from, and what Sync now reports. A code gives the group's key and no way to reach anyone, so the words say what is
/// missing instead of claiming success. The phone's twin (<c>SyncReachText.kt</c>) uses the same wording with "phone" for "PC".
/// Only the three ways that move files by themselves count (sync folder, Google Drive, own relay); QR codes and sync files are by hand.
/// </summary>
public static class SyncReachText
{
    /// <summary>Linked, no automatic method on.</summary>
    public const string NoMethodLine =
        "This PC can't reach your other devices yet. Turn on the sync method they use: a sync folder, Google Drive or your relay.";

    /// <summary>What Sync now says when no automatic method is on.</summary>
    public const string NothingSentLine =
        "No sync method is on, so nothing was sent. Turn on a sync folder, Google Drive or your relay.";

    /// <summary>A method on, nobody heard yet.</summary>
    public static string NothingYetLine(IReadOnlyList<string> methodsOn)
        => $"Nothing has come through {Names(methodsOn)} yet. Your other devices must use the same method.";

    /// <summary>The automatic methods that are on, as <see cref="SyncDeviceCard"/> method names, folder first.</summary>
    public static IReadOnlyList<string> MethodsOn(SyncSettings settings)
    {
        var on = new List<string>();
        if (settings.FolderOn && !string.IsNullOrWhiteSpace(settings.FolderPath))
        {
            on.Add(SyncDeviceCard.MethodFolder);
        }

        if (settings.DriveOn)
        {
            on.Add(SyncDeviceCard.MethodDrive);
        }

        if (settings.RelayOn)
        {
            on.Add(SyncDeviceCard.MethodRelay);
        }

        return on;
    }

    /// <summary>The line for the Devices page while no other device has been heard from.</summary>
    public static string WhileNobodyHeard(SyncSettings settings)
    {
        IReadOnlyList<string> on = MethodsOn(settings);
        return on.Count == 0 ? NoMethodLine : NothingYetLine(on);
    }

    /// <summary>
    /// What Sync now reports after writing and reading: who was reached, or that nobody was and why.
    /// </summary>
    /// <param name="settings">The sync settings as they are now.</param>
    /// <param name="others">The other devices this PC holds (not the removed ones).</param>
    /// <param name="failed">The methods whose write failed; only the ones that are on count.</param>
    /// <param name="passStart">When this Sync now began: a device whose copy changed since is "heard from", the others "last heard from".</param>
    public static string SyncNowLine(
        SyncSettings settings, IReadOnlyList<ReceivedDevice> others, IReadOnlyCollection<string> failed, DateTimeOffset passStart, DateTimeOffset now, TimeZoneInfo zone)
    {
        IReadOnlyList<string> on = MethodsOn(settings);
        if (on.Count == 0)
        {
            return NothingSentLine;
        }

        List<string> sent = on.Where(method => !failed.Contains(method)).ToList();
        List<string> failedOn = on.Where(failed.Contains).ToList();
        string failure = failedOn.Count == 0 ? string.Empty : $"Couldn't send through {Names(failedOn)}. ";
        if (sent.Count == 0)
        {
            return failure + "Nothing was sent.";
        }

        // Heard through a method that is on; a device that last came by QR or a file is not "reached" by Sync now.
        List<ReceivedDevice> heard = others
            .Where(device => on.Contains(device.Method))
            .OrderByDescending(device => device.File.Device.WrittenAt)
            .ToList();
        string sentPart = $"Sent through {Names(sent)}. ";
        if (heard.Count == 0)
        {
            return failure + sentPart + "No other device has answered yet. They must use the same method.";
        }

        string Describe(ReceivedDevice device) => $"{device.File.Device.Name} (sent {When(device.File.Device.WrittenAt, now, zone)})";
        List<string> fresh = heard.Where(device => device.ReceivedAt >= passStart).Select(Describe).ToList();
        List<string> older = heard.Where(device => device.ReceivedAt < passStart).Select(Describe).ToList();
        string reached = fresh.Count == 0 ? string.Empty : "Heard from " + JoinAnd(fresh) + ". ";
        string last = older.Count == 0 ? string.Empty : "Last heard from " + JoinAnd(older) + ".";
        return (failure + sentPart + reached + last).TrimEnd();
    }

    /// <summary>"10:35 AM" today, "yesterday 10:35 AM", "Mon 10:35 AM" within the week, from 7 days back a date ("Oct 12"); 12-hour whatever the system's setting.</summary>
    public static string When(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(at, zone);
        DateTimeOffset today = TimeZoneInfo.ConvertTime(now, zone);
        string time = Clock(local);
        return local.Date == today.Date ? time
            : local.Date == today.Date.AddDays(-1) ? $"yesterday {time}"
            : local.Date <= today.Date.AddDays(-7) ? local.ToString("MMM d", CultureInfo.InvariantCulture)
            : $"{local.ToString("ddd", CultureInfo.InvariantCulture)} {time}";
    }

    /// <summary>"4:05 PM": 12-hour with AM/PM whatever the system's 24-hour setting.</summary>
    public static string Clock(DateTimeOffset local) => local.ToString("h:mm tt", CultureInfo.InvariantCulture);

    private static string Names(IReadOnlyList<string> methods)
        => JoinAnd(methods.Select(NameOf).ToList());

    private static string NameOf(string method) => method switch
    {
        SyncDeviceCard.MethodFolder => "the sync folder",
        SyncDeviceCard.MethodDrive => "Google Drive",
        SyncDeviceCard.MethodRelay => "your relay",
        _ => "a sync file",
    };

    private static string JoinAnd(IReadOnlyList<string> items)
        => items.Count <= 1 ? string.Concat(items) : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];
}
