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

    /// <returns>Null when the session started, otherwise why not.</returns>
    Task<string?> StartAsync(LinkStartRequest request);

    /// <returns>What ending the running session early takes, or null when no session runs.</returns>
    Task<LinkEndEarlyTerms?> GetEndEarlyTermsAsync();

    /// <summary>Ends the running session with this reason; the link has already checked the wait and the length.</summary>
    /// <returns>Null when it ended, otherwise why not.</returns>
    Task<string?> EndEarlyAsync(string reason);
}
