using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.App.Views;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Opens the Google setup guide window and remembers its progress (documents "googleSetup"). The guide saves the
/// client ID and secret where Settings → Calendars keeps them, and tests by connecting Google Calendar.
/// Use from the UI thread only.
/// </summary>
public sealed class GoogleSetupGuide
{
    private const string ProgressKey = "googleSetup";

    private readonly CalendarService _calendars;
    private readonly CalendarSyncService _sync;
    private readonly IDataProtector _protector;
    private readonly SqliteJsonDocumentStore _store;
    private readonly Func<Uri, bool> _openBrowser;

    private GoogleSetupWindow? _window;

    public GoogleSetupGuide(
        CalendarService calendars,
        CalendarSyncService sync,
        IDataProtector protector,
        SqliteJsonDocumentStore store,
        Func<Uri, bool> openBrowser)
    {
        _calendars = calendars;
        _sync = sync;
        _protector = protector;
        _store = store;
        _openBrowser = openBrowser;
    }

    /// <summary>Raised after the guide saved a client ID and secret.</summary>
    public event EventHandler? ClientSaved;

    /// <param name="includeSleep">Start with sleep tracking ticked (opened from Settings → Sleep).</param>
    public void Show(bool includeSleep)
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        GoogleSetupProgress progress = LoadProgress();
        var viewModel = new GoogleSetupViewModel(
            progress with { IncludeSleep = progress.IncludeSleep || includeSleep },
            _calendars.Settings.GoogleClientId,
            _calendars.Settings.ProtectedGoogleClientSecret is not null,
            SaveClient,
            ConnectCalendarAsync,
            _openBrowser);
        viewModel.ProgressChanged += (_, changed) => SaveProgress(changed);

        _window = new GoogleSetupWindow { DataContext = viewModel };
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }

    private void SaveClient(string clientId, string? clientSecret)
    {
        string? secret = string.IsNullOrWhiteSpace(clientSecret)
            ? _calendars.Settings.ProtectedGoogleClientSecret
            : Convert.ToBase64String(_protector.Protect(clientSecret.Trim()));
        _calendars.SaveSettings(_calendars.Settings with { GoogleClientId = clientId.Trim(), ProtectedGoogleClientSecret = secret });
        ClientSaved?.Invoke(this, EventArgs.Empty);
    }

    private Task<string?> ConnectCalendarAsync()
    {
        return _sync.ConnectGoogleAsync(_openBrowser, CancellationToken.None);
    }

    private GoogleSetupProgress LoadProgress()
    {
        try
        {
            GoogleSetupProgress? saved = Task.Run(() => _store.LoadAsync<GoogleSetupProgress>(ProgressKey)).GetAwaiter().GetResult();
            return saved is null || saved.DoneStepIds is null ? GoogleSetupProgress.Empty : saved;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            return GoogleSetupProgress.Empty;
        }
    }

    private async void SaveProgress(GoogleSetupProgress progress)
    {
        try
        {
            await _store.SaveAsync(ProgressKey, progress);
        }
        catch (SqliteException)
        {
            // Remembering progress is a convenience; the guide still works.
        }
    }
}
