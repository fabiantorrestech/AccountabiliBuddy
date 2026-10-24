using AccountabiliBuddy.Core.Storage;
using Avalonia;
using Avalonia.Styling;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The main window's look: as Windows (the default), light or dark (app-owned document "theme"). The on-screen window
/// and the quick-session popup stay dark on purpose. Use from the UI thread.
/// </summary>
public sealed class AppTheme
{
    private const string DocumentKey = "theme";

    public static IReadOnlyList<string> Names { get; } = ["As Windows", "Light", "Dark"];

    private readonly SqliteJsonDocumentStore _store;

    public AppTheme(SqliteJsonDocumentStore store)
    {
        _store = store;
    }

    /// <summary>0 = as Windows, 1 = light, 2 = dark.</summary>
    public int Choice { get; private set; }

    public void LoadBlocking()
    {
        try
        {
            Choice = Math.Clamp(Task.Run(() => _store.LoadAsync<StoredTheme>(DocumentKey)).GetAwaiter().GetResult()?.Choice ?? 0, 0, 2);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            Choice = 0;
        }

        Apply();
    }

    public async Task SetAsync(int choice)
    {
        Choice = Math.Clamp(choice, 0, 2);
        Apply();
        try
        {
            await _store.SaveAsync(DocumentKey, new StoredTheme(Choice));
        }
        catch (SqliteException)
        {
            // Applies for now; written with the next change.
        }
    }

    private void Apply()
    {
        if (Application.Current is Application app)
        {
            app.RequestedThemeVariant = Choice switch
            {
                1 => ThemeVariant.Light,
                2 => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }

    private sealed record StoredTheme(int Choice);
}
