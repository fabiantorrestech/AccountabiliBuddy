namespace AccountabiliBuddy.App.Services;

/// <summary>The data-related permissions granted in the Setup tab. App-owned (not part of the shared profile schema).</summary>
/// <param name="ScreenReadingEnabled">OCR of unrecognised windows during focus. Off until granted.</param>
/// <param name="ActivityLogEnabled">Local activity history. On by default; revocable.</param>
public sealed record PrivacySettings(bool ScreenReadingEnabled, bool ActivityLogEnabled)
{
    public static PrivacySettings Default { get; } = new(ScreenReadingEnabled: false, ActivityLogEnabled: true);
}
