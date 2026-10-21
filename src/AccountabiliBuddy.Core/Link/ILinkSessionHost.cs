using System.Text.Json;

namespace AccountabiliBuddy.Core.Link;

/// <summary>
/// What the link needs from the app: the session to show, starting one, and ending one early by the app's own rules.
/// Called from the link's threads; implementations move the work to wherever the app keeps its session.
/// </summary>
public interface ILinkSessionHost
{
    /// <summary>The PC's name as devices show it.</summary>
    string PcName { get; }

    /// <summary>The app's trusted time (not the possibly changed Windows clock).</summary>
    DateTimeOffset Now { get; }

    /// <summary>Raised whenever what <see cref="GetStateAsync"/> returns changed (a session started, ended or moved phase; profiles changed).</summary>
    event EventHandler? StateChanged;

    Task<LinkState> GetStateAsync();

    /// <summary>Raised when what <see cref="GetSettingsAsync"/> returns may have changed.</summary>
    event EventHandler? SettingsChanged;

    /// <summary>
    /// The profiles, sleep and day bar settings and upcoming events the device shows, in a backup's shape
    /// (profiles as the ADR 7 document string, "settings" with "sleep" and "daybar", "calendarEvents").
    /// </summary>
    Task<JsonElement> GetSettingsAsync();

    /// <summary>Raised when what <see cref="GetFocusDayAsync"/> returns may have changed.</summary>
    event EventHandler? FocusDayChanged;

    /// <summary>Today's focus (ADR 21): the plan's parts, the sessions that count, drift, and totals for today and the week.</summary>
    Task<LinkFocusDay> GetFocusDayAsync();

    /// <returns>Null when the session started, otherwise why not.</returns>
    Task<string?> StartAsync(LinkStartRequest request);

    /// <returns>What ending the running session early takes, or null when no session runs.</returns>
    Task<LinkEndEarlyTerms?> GetEndEarlyTermsAsync();

    /// <summary>Takes a day plan edit from the device (ADR 19) by the PC's own rules, saving it when it applies.</summary>
    Task<LinkDayPlanAnswer> ApplyDayPlanAsync(LinkDayPlanEdit edit);

    /// <summary>Ends the running session with this reason; the link has already checked the wait and the length.</summary>
    /// <returns>Null when it ended, otherwise why not.</returns>
    Task<string?> EndEarlyAsync(string reason);

    /// <summary>Raised when what <see cref="GetSyncFilesAsync"/> returns may have changed.</summary>
    event EventHandler? SyncFilesChanged;

    /// <summary>
    /// The sealed sync files (ADR 22) paired phones get: this PC's own and the last one received from each other device,
    /// as it arrived. The same bytes until a file changes, since the link sends each file once per connection. Empty
    /// outside a device group or while sharing over the link is off.
    /// </summary>
    Task<IReadOnlyList<byte[]>> GetSyncFilesAsync();

    /// <summary>Takes a sealed sync file a phone carried (ADR 22) by the PC's own rules.</summary>
    /// <returns>Null when it was taken, otherwise why not (e.g. this PC isn't in a device group).</returns>
    Task<string?> AcceptSyncFileAsync(byte[] sealedBytes);
}
