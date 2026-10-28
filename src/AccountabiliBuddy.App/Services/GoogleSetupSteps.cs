namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The steps of the Google setup guide, with every Google Cloud Console link and scope in one place, so they can be
/// fixed here when Google moves a page or renames a scope. The steps set up the user's own Google Cloud project
/// for read-only Calendar access (ADR 9), Drive sync for this computer and the phone (ADR 26) and, when wanted,
/// read-only sleep data (ADR 11).
/// </summary>
public static class GoogleSetupSteps
{
    public const string AppName = "AccountabiliBuddy";
    public const string CalendarScope = GoogleCalendarClient.Scope;
    public const string SleepScope = "https://www.googleapis.com/auth/googlehealth.sleep.readonly";
    public const string DriveScope = AccountabiliBuddy.App.Services.Sync.DriveAccountService.Scope;

    /// <summary>The phone app's package name, for its Android client.</summary>
    public const string PhonePackage = "com.accountabilibuddy.companion";

    /// <summary>
    /// The SHA-1 of the key the phone app is signed with when built on the developer's PC (debug and release builds use
    /// the same key). A build signed elsewhere shows its own in the phone's Devices → Google Drive.
    /// </summary>
    public const string PhoneSha1 = "96:76:77:E7:FC:87:65:0E:36:CA:30:A1:29:58:59:81:35:6C:CB:16";

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
                "The account whose calendar (and watch) you link is picked later, when you sign in at the end or in Plan › Calendar; it can be a different one.",
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
                "Drive sync (Settings › Devices & sync → Google Drive) keeps your devices' encrypted sync files in one folder the app makes; it can't see your other files.",
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
                "Open the page. The publishing status starts on Testing (the last step can publish it, so sign-ins stop ending).",
                "Under Test users press Add users, type the email of every Google account you'll sign in with (e.g. your calendar's, if it isn't this one), Save.",
                "While the project is in Testing, Google ends the sign-in after 7 days; the step \"Stop the 7-day sign-out\" below ends that.",
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
            "phone-client",
            "Let the phone sign in to Google Drive (no ID or secret on the phone)",
            new[]
            {
                "Skip this if you don't use the phone app's Google Drive switch (Devices → Google Drive on the phone).",
                "The phone has no box for a client ID or secret and needs none: Google recognises the app by its package name and signing key, registered here as an Android client.",
                "Open the page. Application type: Android.",
                $"Name: {AppName} phone. Package name and SHA-1 certificate fingerprint: copy them from below. Press Create.",
                "Nothing to paste anywhere: the phone finds this client by its package name and key. It uses the same project, so the Drive API, the drive.file scope and your test users above already apply to it.",
                "On the phone, turn on Devices → Google Drive and pick an account that's a test user. \"Google Drive wasn't allowed\" means this client, the Drive API or the test user is missing.",
            },
            "https://console.cloud.google.com/auth/clients/create",
            new[]
            {
                new GoogleSetupCopyValue("Client name", $"{AppName} phone"),
                new GoogleSetupCopyValue("Package name", PhonePackage),
                new GoogleSetupCopyValue("SHA-1 certificate fingerprint", PhoneSha1),
            }),

        new GoogleSetupStep(
            "publish",
            "Stop the 7-day sign-out (recommended)",
            new[]
            {
                "Open the page and press Publish app, then Confirm. The status changes from Testing to In production.",
                "Google then stops ending your sign-in every 7 days, so you connect once.",
                "No review is needed for your own use: at sign-in Google may say it hasn't verified the app. It's your own project: press Advanced, then Go to AccountabiliBuddy.",
            },
            "https://console.cloud.google.com/auth/audience",
            Array.Empty<GoogleSetupCopyValue>()),

        new GoogleSetupStep(
            TestId,
            "Connect and test",
            new[]
            {
                "Press Connect a Google account. Your browser opens Google's sign-in: pick the account whose calendar you want (any of the test users).",
                "Google warns that it hasn't verified the app: that's expected for your own project. Press Continue.",
                "Allow the read-only access. Your calendars then show in Plan, under Calendar.",
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
