namespace AccountabiliBuddy.Core.Ports;

/// <summary>Private copies of Cold Turkey's databases (it keeps the originals open). Disposing deletes the copies.</summary>
/// <param name="Folder">The temporary folder holding the copies.</param>
/// <param name="SettingsDatabase">data-app.db: blocks and schedules.</param>
/// <param name="BrowserStatsDatabase">data-browser.db: time per site, if present.</param>
/// <param name="AppStatsDatabase">data-helper.db: time per app, if present.</param>
public sealed record ColdTurkeyDataFiles(string Folder, string SettingsDatabase, string? BrowserStatsDatabase, string? AppStatsDatabase)
    : IDisposable
{
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, recursive: true);
            }
        }
        catch (IOException)
        {
            // Left for the system's temp cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Left for the system's temp cleanup.
        }
    }
}
