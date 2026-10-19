namespace AccountabiliBuddy.App.Services;

/// <summary>Google's sign-in and Calendar API addresses; replaceable so checks can run against a local stand-in.</summary>
public sealed record GoogleEndpoints(Uri Authorize, Uri Token, Uri Revoke, Uri CalendarApi)
{
    public static GoogleEndpoints Default { get; } = new(
        new Uri("https://accounts.google.com/o/oauth2/v2/auth"),
        new Uri("https://oauth2.googleapis.com/token"),
        new Uri("https://oauth2.googleapis.com/revoke"),
        new Uri("https://www.googleapis.com/calendar/v3/"));
}
