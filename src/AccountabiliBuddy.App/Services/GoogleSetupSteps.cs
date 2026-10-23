namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The steps of the Google setup guide, with every Google Cloud Console link and scope in one place, so they can be
/// fixed here when Google moves a page or renames a scope. The steps set up the user's own Google Cloud project
/// for read-only Calendar access (ADR 9) and, when wanted, read-only sleep data (ADR 11).
/// </summary>
public static class GoogleSetupSteps
{
    public const string AppName = "AccountabiliBuddy";
    public const string CalendarScope = GoogleCalendarClient.Scope;
    public const string SleepScope = "https://www.googleapis.com/auth/googlehealth.sleep.readonly";
    public const string DriveScope = AccountabiliBuddy.App.Services.Sync.DriveAccountService.Scope;

    public const string PasteClientId = "paste-client";
    public const string TestId = "test";

    public static IReadOnlyList<GoogleSetupStep> All { get; } = new[]
    {
        new GoogleSetupStep(
            "project",
            "Create a Google Cloud project",
            new[]
            {
                "Open the page and sign in with any Google account you own: it holds the project. Use the same one on every page after this.",
                "The account whose calendar (and watch) you link is picked later, when you sign in at the end or on the Calendar tab; it can be a different one.",
                $"Project name: {AppName}. Leave the rest as it is and press Create.",
                "Wait a few seconds, then make sure the new project is selected in the bar at the top of the page.",
                "Already have a project for AccountabiliBuddy? Just select it and go on.",
            },
            "https://console.cloud.google.com/projectcreate",
            new[] { new GoogleSetupCopyValue("Project name", AppName) }),

        new GoogleSetupStep(
            "calendar-api",
            "Turn on the Google Calendar API",
            new[]
            {
                "Open the page (check the project at the top is yours).",
                "Press Enable. If it says Manage instead, it's already on.",
            },
            "https://console.cloud.google.com/apis/library/calendar-json.googleapis.com",
            Array.Empty<GoogleSetupCopyValue>()),

        new GoogleSetupStep(
            "drive-api",
            "Turn on the Google Drive API (for Drive sync)",
            new[]
            {
                "Open the page and press Enable. If it says Manage instead, it's already on.",
                "Drive sync (Devices → Google Drive) keeps your devices' encrypted sync files in one folder the app makes; it can't see your other files.",
            },
            "https://console.cloud.google.com/apis/library/drive.googleapis.com",
            Array.Empty<GoogleSetupCopyValue>()),

        new GoogleSetupStep(
            "health-api",
            "Turn on the Google Health API",
            new[]
            {
                "Open the page and press Enable.",
                "If the page can't find it, search the API Library for \"Google Health API\" (not Google Fit, which is being retired).",
                "This lets the app read sleep from your Fitbit or Pixel Watch.",
            },
            "https://console.cloud.google.com/apis/library/health.googleapis.com",
            Array.Empty<GoogleSetupCopyValue>(),
            ForSleepOnly: true),

        new GoogleSetupStep(
            "branding",
            "Name the app (sign-in screen)",
            new[]
            {
                "Open the page and press Get started.",
                $"App name: {AppName}. User support email: your email. Next.",
                "Audience: External. Next.",
                "Contact information: your email. Next, agree to the policy, Create.",
                "Only you will ever see this sign-in screen; nothing is published.",
            },
            "https://console.cloud.google.com/auth/branding",
            new[] { new GoogleSetupCopyValue("App name", AppName) }),

        new GoogleSetupStep(
            "test-users",
            "Add yourself as a test user",
            new[]
            {
                "Open the page. Leave the publishing status on Testing.",
                "Under Test users press Add users, type the email of every Google account you'll sign in with (e.g. your calendar's, if it isn't this one), Save.",
                "While the project is in Testing, Google ends the sign-in after 7 days; the app tells you when to connect again.",
            },
            "https://console.cloud.google.com/auth/audience",
            Array.Empty<GoogleSetupCopyValue>()),

        new GoogleSetupStep(
            "scopes",
            "Allow read-only access",
            new[]
            {
                "Open the page and press Add or remove scopes.",
                "At the bottom, in Manually add scopes, paste the scope(s) below (one per line), press Add to table, then Update.",
                "Press Save. Calendar and sleep are read-only: the app can never change them. drive.file reaches only the files the app made.",
            },
            "https://console.cloud.google.com/auth/scopes",
            new[]
            {
                new GoogleSetupCopyValue("Calendar (read-only)", CalendarScope),
                new GoogleSetupCopyValue("Sleep (read-only)", SleepScope),
                new GoogleSetupCopyValue("Drive sync (only the app's files)", DriveScope),
            }),

        new GoogleSetupStep(
            "client",
            "Create a Desktop client",
            new[]
            {
                "Open the page. Application type: Desktop app.",
                $"Name: {AppName} desktop. Press Create.",
                "A box shows the Client ID and Client secret. Keep it open for the next step (you can also find them later under Clients).",
            },
            "https://console.cloud.google.com/auth/clients/create",
            new[] { new GoogleSetupCopyValue("Client name", $"{AppName} desktop") }),

        new GoogleSetupStep(
            PasteClientId,
            "Paste the client ID and secret",
            new[]
            {
                "Copy the Client ID and Client secret from Google into the boxes below and press Save.",
                "The secret is stored encrypted on this computer only.",
            },
            null,
            Array.Empty<GoogleSetupCopyValue>()),

        new GoogleSetupStep(
            TestId,
            "Connect and test",
            new[]
            {
                "Press Connect a Google account. Your browser opens Google's sign-in: pick the account whose calendar you want (any of the test users).",
                "Google warns that it hasn't verified the app: that's expected for your own project. Press Continue.",
                "Allow the read-only access. Your calendars then show in the Calendar tab.",
            },
            null,
            Array.Empty<GoogleSetupCopyValue>()),
    };

    /// <summary>The steps for what the user wants to set up.</summary>
    public static IReadOnlyList<GoogleSetupStep> For(bool includeSleep)
    {
        return All.Where(step => includeSleep || !step.ForSleepOnly)
            .Select(step => includeSleep || step.Id != "scopes"
                ? step
                : step with { CopyValues = step.CopyValues.Where(value => value.Value != SleepScope).ToList() })
            .ToList();
    }

    /// <summary>
    /// The step that most likely fixes a Google sign-in or API error, by the error's text; null when unknown.
    /// </summary>
    public static string? StepForError(string error)
    {
        string text = error.ToLowerInvariant();
        if (text.Contains("access_denied") || text.Contains("access blocked") || text.Contains("test user"))
        {
            return "test-users";
        }

        if (text.Contains("invalid_client") || text.Contains("client id") || text.Contains("unauthorized_client"))
        {
            return PasteClientId;
        }

        if (text.Contains("redirect_uri") || text.Contains("redirect uri"))
        {
            return "client";
        }

        if (text.Contains("invalid_scope") || text.Contains("scope"))
        {
            return "scopes";
        }

        if (text.Contains("has not been used") || text.Contains("disabled") || text.Contains("accessnotconfigured") || text.Contains("403"))
        {
            return "calendar-api";
        }

        return null;
    }
}
