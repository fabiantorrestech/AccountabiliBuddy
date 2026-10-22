namespace AccountabiliBuddy.App.Services;

/// <summary>The data-related permissions granted in the Setup tab. App-owned (not part of the shared profile schema).</summary>
/// <param name="ScreenReadingEnabled">OCR of unrecognised windows during focus. Off until granted.</param>
/// <param name="ActivityLogEnabled">Local activity history. On by default; revocable.</param>
/// <param name="KeepTitlesAndAddresses">
/// False (the Work computer type): History keeps only times, apps, sites and their sections; window titles, full
/// addresses and screen text are used in memory to spot distractions but never stored, not even encrypted.
/// </param>
public sealed record PrivacySettings(bool ScreenReadingEnabled, bool ActivityLogEnabled, bool KeepTitlesAndAddresses = true)
{
    public static PrivacySettings Default { get; } = new(ScreenReadingEnabled: false, ActivityLogEnabled: true);
}
