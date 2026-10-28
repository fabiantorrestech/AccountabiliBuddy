using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The Google Drive account sync uses (ADR 26): signed in with the user's own OAuth client (the one Settings → Calendars
/// keeps for Google Calendar) and the drive.file scope only. The refresh token is kept DPAPI-encrypted (documents key
/// "sync.drive"); access tokens live in memory and are refreshed shortly before they expire. Use from the UI thread only.
/// </summary>
public sealed class DriveAccountService
{
    public const string Scope = "https://www.googleapis.com/auth/drive.file";
    private const string DocumentKey = "sync.drive";
    private static readonly TimeSpan RefreshEarly = TimeSpan.FromMinutes(1);

    private readonly GoogleCalendarClient _google;
    private readonly CalendarService _calendars;
    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private DriveAccountState _state = new(null, null);
    private string? _accessToken;
    private DateTimeOffset _accessExpiresAt;

    public DriveAccountService(GoogleCalendarClient google, CalendarService calendars, SqliteJsonDocumentStore store, IDataProtector protector, TimeProvider time)
    {
        _google = google;
        _calendars = calendars;
        _store = store;
        _protector = protector;
        _time = time;
    }

    public bool IsSignedIn => _state.ProtectedRefreshToken is not null;

    /// <summary>When the account was signed in; null when it isn't.</summary>
    public DateTimeOffset? SignedInAt => _state.SignedInAt;

    /// <summary>True when Settings → Calendars has a Google client ID and secret (sign-in needs them).</summary>
    public bool HasClient => TryGetClient(out _, out _);

    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            _state = Task.Run(() => _store.LoadAsync<DriveAccountState>(DocumentKey)).GetAwaiter().GetResult() ?? new DriveAccountState(null, null);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _state = new DriveAccountState(null, null);
        }
    }

    /// <summary>Signs in through the browser, asking only for drive.file.</summary>
    /// <exception cref="GoogleSignInException">No client set up, cancelled, denied or timed out.</exception>
    public async Task SignInAsync(Func<Uri, bool> openBrowser, CancellationToken cancellationToken)
    {
        if (!TryGetClient(out string clientId, out string clientSecret))
        {
            throw new GoogleSignInException("Set up your Google client first (Settings › Your day › Calendars › Google setup guide).");
        }

        GoogleTokens tokens = await _google.SignInAsync(clientId, clientSecret, openBrowser, cancellationToken, Scope);
        _accessToken = tokens.AccessToken;
        _accessExpiresAt = tokens.AccessTokenExpiresAt;
        await SaveAsync(new DriveAccountState(Convert.ToBase64String(_protector.Protect(tokens.RefreshToken)), _time.GetUtcNow()));
    }

    /// <summary>Signs out: revokes the token with Google (best effort) and forgets it.</summary>
    public async Task SignOutAsync()
    {
        string? refresh = RefreshToken();
        _accessToken = null;
        await SaveAsync(new DriveAccountState(null, null));
        if (refresh is not null)
        {
            try
            {
                await _google.RevokeAsync(refresh, CancellationToken.None);
            }
            catch (HttpRequestException)
            {
                // Forgotten here either way; Google drops unused tokens after a while.
            }
        }
    }

    /// <summary>A fresh access token for Drive.</summary>
    /// <exception cref="InvalidOperationException">Not signed in, or Google ended the sign-in.</exception>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _time.GetUtcNow() < _accessExpiresAt - RefreshEarly)
        {
            return _accessToken;
        }

        string refresh = RefreshToken() ?? throw new InvalidOperationException("Not signed in to Google Drive.");
        if (!TryGetClient(out string clientId, out string clientSecret))
        {
            throw new InvalidOperationException("The Google client is gone from Settings › Your day › Calendars.");
        }

        try
        {
            (string token, DateTimeOffset expiresAt) = await _google.RefreshAccessTokenAsync(clientId, clientSecret, refresh, cancellationToken);
            _accessToken = token;
            _accessExpiresAt = expiresAt;
            return token;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("Google ended the Drive sign-in: sign in again in Devices.", ex);
        }
    }

    private string? RefreshToken()
    {
        return _state.ProtectedRefreshToken is string stored ? _protector.Unprotect(Convert.FromBase64String(stored)) : null;
    }

    private bool TryGetClient(out string clientId, out string clientSecret)
    {
        clientId = _calendars.Settings.GoogleClientId ?? string.Empty;
        string? protectedSecret = _calendars.Settings.ProtectedGoogleClientSecret;
        clientSecret = protectedSecret is null ? string.Empty : _protector.Unprotect(Convert.FromBase64String(protectedSecret)) ?? string.Empty;
        return clientId.Length > 0 && clientSecret.Length > 0;
    }

    private async Task SaveAsync(DriveAccountState state)
    {
        _state = state;
        Changed?.Invoke(this, EventArgs.Empty);
        await _store.SaveAsync(DocumentKey, state);
    }
}
