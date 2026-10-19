namespace AccountabiliBuddy.App.Services;

/// <summary>Calendar preferences of this computer (documents key "calendar.settings").</summary>
/// <param name="LinkRefreshMinutes">How often iCal links are checked; never less than <see cref="MinLinkRefreshMinutes"/>.</param>
/// <param name="GoogleClientId">The user's own Google Cloud OAuth client id (desktop app type).</param>
/// <param name="ProtectedGoogleClientSecret">Its client secret, DPAPI-encrypted, base64.</param>
public sealed record CalendarSettings(
    int LinkRefreshMinutes = CalendarSettings.MinLinkRefreshMinutes,
    string? GoogleClientId = null,
    string? ProtectedGoogleClientSecret = null)
{
    public const int MinLinkRefreshMinutes = 5;

    public static CalendarSettings Default { get; } = new();
}
