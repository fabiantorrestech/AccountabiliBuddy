using AccountabiliBuddy.App.Services.Sync;

namespace AccountabiliBuddy.App.Services.Setup;

/// <summary>
/// "What this computer will do": what the app will access on this PC with a type's switches, in plain words, so the
/// user can show IT if needed, and what it doesn't do.
/// </summary>
public static class ComputerTypeSummary
{
    public static IReadOnlyList<string> Lines(PresetValues values, SyncSettings sync)
    {
        var lines = new List<string>
        {
            values.KeepTitlesAndAddresses
                ? "Reads the title of the window in front, to spot distractions; titles are kept in History, encrypted"
                : "Reads the title of the window in front, to spot distractions; titles aren't stored",
            values.KeepTitlesAndAddresses
                ? "The browser extension reads the address of the open tab, to spot distracting sites; the address is kept in History, encrypted"
                : "The browser extension reads the address of the open tab, to spot distracting sites; only the site is stored",
        };
        if (values.FolderOn)
        {
            lines.Add(sync.FolderPath is null
                ? "Writes an encrypted file to a folder you choose, every 15 minutes"
                : $"Writes an encrypted file to the folder you chose, {IntervalText(sync.Interval)}");
        }

        if (values.QrOn)
        {
            lines.Add("Shows QR codes for your phone to scan, and reminds you when work ends");
        }

        lines.Add("Starts with Windows");
        if (values.CheckForUpdates)
        {
            lines.Add("Checks GitHub for updates");
        }

        if (values.GoogleCalendarOn)
        {
            lines.Add("Reads Google Calendar if you sign in");
            lines.Add("Shares with your phone through Google Drive once you sign in (Devices).");
        }

        // The app's own connections: update checks and Google sign-in go out (a sync folder is the folder's own app's
        // business, e.g. Google Drive for desktop).
        if (!values.CheckForUpdates && !values.GoogleCalendarOn)
        {
            lines.Add("Makes no network connections of its own");
        }

        if (!values.ScreenReading)
        {
            lines.Add("Doesn't read your screen.");
        }

        lines.Add("Doesn't listen for connections.");

        if (!values.CheckForUpdates && !values.GoogleCalendarOn)
        {
            lines.Add("Doesn't send anything to the internet itself.");
        }

        return lines;
    }

    private static string IntervalText(SyncInterval interval)
    {
        return interval switch
        {
            SyncInterval.Instant => "on every change",
            SyncInterval.OneMinute => "every minute",
            SyncInterval.FiveMinutes => "every 5 minutes",
            _ => "every 15 minutes",
        };
    }
}
