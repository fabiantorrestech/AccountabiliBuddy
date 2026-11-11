using System.Globalization;
using System.Net;
using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// Exports task reminders and due dates to Google Calendar (ADR 78): one event per task whose "Add to Google Calendar" switch is on,
/// in the app's own "AB Reminders" calendar (or, with the experimental option, due-dated tasks in a calendar the user picked).
/// <list type="bullet">
/// <item>Only the task's title and times leave the PC (<see cref="TaskCalendarMapping"/>, <see cref="GoogleCalendarWriter"/>); every
/// call goes through <see cref="CalendarNetwork"/>.</item>
/// <item>Needs the connected account's consent to <see cref="GoogleCalendarClient.AppCreatedScope"/> (<see cref="AllowExportAsync"/>);
/// until then nothing is written and calendars are read as before. The picked calendar is the account's setting, the same on every
/// device (<see cref="TaskAccountSettingsService.PickedCalendarId"/>), but each PC needs <see cref="GoogleCalendarClient.EventsScope"/>
/// of its own (<see cref="AllowPickedCalendarAsync"/>). Without it nothing is written there: a pick made on this PC keeps its tasks'
/// events where they are until consent is given; a pick made on another device (which had the consent) leaves its tasks to the devices
/// that can write there, and this PC deletes its own "AB Reminders" events for them, so no task gets two events.</item>
/// <item>Writes follow every change of the tasks or the settings, a moment later, in the background; a failure waits and tries
/// again (30 s, doubling to 30 min) and never stops the app. Only events recorded in <see cref="TaskCalendarExportState"/> are
/// changed or deleted.</item>
/// <item>The log ("tasks-calendar.log" in the data folder) holds counts and status codes only.</item>
/// </list>
/// The same in both modes. Use from the UI thread only.
/// </summary>
public sealed class TaskCalendarExportService : IDisposable
{
    public const string StateKey = "tasks.calendarExport";
    public const string AppCalendarName = "AB Reminders";
    public const string LogFileName = "tasks-calendar.log";

    private const string AppConsentText = "Allow adding reminders to Google Calendar in Settings (Google asks once).";
    private const string PickedConsentText =
        "Allow changing your calendars' events (again) to use the picked calendar; until then the app doesn't write there.";
    private const string PickedElsewhereText =
        "Tasks with a due date go to the calendar picked on another device; this PC leaves them to the devices allowed to write there "
        + "(allow changing your calendars' events here to add them from this PC too).";

    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRetry = TimeSpan.FromMinutes(30);

    private readonly TaskStore _store;
    private readonly TaskReminderSettingsService _settings;
    private readonly TaskAccountSettingsService _account;
    private readonly CalendarService _calendars;
    private readonly GoogleCalendarClient _google;
    private readonly GoogleCalendarWriter _writer;
    private readonly IDataProtector _protector;
    private readonly SqliteJsonDocumentStore _documents;
    private readonly TimeProvider _time;
    private readonly string? _logPath;

    // Events Google refused as they are (400): not tried again until the task's event changes.
    private readonly HashSet<(Guid TaskId, string Signature)> _refused = [];

    private TaskCalendarExportState _state = TaskCalendarExportState.Empty;
    private GoogleAccessToken? _access;
    private string? _accessSourceId;
    private DispatcherTimer? _timer;
    private bool _running;
    private bool _again;
    private TimeSpan? _retryWait;

    // "AB Reminders" was gone or refused this many times in a row: the first time it is made again at once, after that only once per
    // backoff wait, so a calendar Google keeps refusing never makes a new calendar every few seconds.
    private int _appCalendarTrouble;

    private bool _loggedMarkedHide;
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;

    // The count last logged for tasks left to the devices that may write in a calendar picked elsewhere (logged when it changes).
    private int _leftElsewhereLogged = -1;

    public TaskCalendarExportService(
        TaskStore store,
        TaskReminderSettingsService settings,
        CalendarService calendars,
        GoogleCalendarClient google,
        GoogleCalendarWriter writer,
        IDataProtector protector,
        SqliteJsonDocumentStore documents,
        TimeProvider time,
        string? logPath,
        TaskAccountSettingsService? account = null)
    {
        _store = store;
        _settings = settings;
        _account = account ?? new TaskAccountSettingsService(store);
        _calendars = calendars;
        _google = google;
        _writer = writer;
        _protector = protector;
        _documents = documents;
        _time = time;
        _logPath = logPath;
    }

    /// <summary>Why export isn't working now, for Settings (M7); null when it is (or nothing is to be exported).</summary>
    public string? Problem { get; private set; }

    /// <summary>The account hasn't allowed adding events yet (existing users, until they press Allow in Settings).</summary>
    public bool NeedsConsent { get; private set; }

    /// <summary><see cref="Problem"/> or <see cref="NeedsConsent"/> changed.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>"Sync task lists" is on (set by the app): only then does another device's pick count here.</summary>
    public Func<bool> SharesTaskLists { get; set; } = () => true;

    /// <summary>The events this PC wrote, by task id (ids and hashes only).</summary>
    public IReadOnlyDictionary<Guid, TaskCalendarRecord> Records => _state.Records;

    /// <summary>
    /// Whether the Google calendar is the app's own "AB Reminders" (the reading side leaves it out: its events are tasks). By its
    /// recorded id only, so a calendar of the user's that happens to have the name is still read.
    /// </summary>
    /// <remarks>
    /// A PC that has no record of it (export off here, a new install) knows it by its name and the description only the app writes
    /// (<see cref="GoogleCalendarWriter.AppCalendarDescription"/>, set when it is made or confirmed): a read-only check, no write. The
    /// first time it is hidden that way the log says so.
    /// </remarks>
    public bool IsAppCalendar(GoogleCalendarEntry calendar)
    {
        if (_state.AppCalendarId is { } id)
        {
            return calendar.Id == id;
        }

        bool marked = calendar.Summary == AppCalendarName && calendar.Description == GoogleCalendarWriter.AppCalendarDescription;
        if (marked && !_loggedMarkedHide)
        {
            _loggedMarkedHide = true;
            Log("AB Reminders hidden from reading by its description (no record of it on this PC)");
        }

        return marked;
    }

    public void LoadBlocking()
    {
        try
        {
            _state = Task.Run(() => _documents.LoadAsync<TaskCalendarExportState>(StateKey)).GetAwaiter().GetResult() ?? TaskCalendarExportState.Empty;
            _state = _state with { Records = _state.Records ?? new Dictionary<Guid, TaskCalendarRecord>() };
        }
        catch (Exception ex) when (ex is SqliteException or JsonException)
        {
            _state = TaskCalendarExportState.Empty;
        }
    }

    /// <summary>Follows the tasks and the settings from now on, and exports what changed while the app was closed.</summary>
    public void Start()
    {
        _timer = new DispatcherTimer { Interval = Debounce };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            _ = SyncNowAsync();
        };
        _store.Changed += (_, _) => Soon();
        // A changed setting ("Tasks with a date but no time") re-exports the events it changes, in place; so does a change of the
        // account's picked calendar, made here or on another device (it moves the events it concerns).
        _settings.Changed += (_, _) => Soon();
        _account.Changed += (_, _) => Soon();
        Soon();
    }

    public void Dispose()
    {
        _timer?.Stop();
    }

    /// <summary>
    /// Asks the connected Google account to allow adding events to calendars the app makes (incremental consent: reading stays
    /// allowed). Opens Google's page in the browser.
    /// </summary>
    /// <returns>Null when allowed; otherwise why not.</returns>
    public Task<string?> AllowExportAsync(Func<Uri, bool> openBrowser, CancellationToken cancellationToken)
    {
        return AllowAsync(includeEvents: false, openBrowser, cancellationToken);
    }

    /// <summary>The experimental option: also asks to change events in the user's own calendars (for the picked calendar).</summary>
    /// <returns>Null when allowed; otherwise why not.</returns>
    public Task<string?> AllowPickedCalendarAsync(Func<Uri, bool> openBrowser, CancellationToken cancellationToken)
    {
        return AllowAsync(includeEvents: true, openBrowser, cancellationToken);
    }

    /// <summary>
    /// Turns the experimental option on for <paramref name="googleCalendarId"/> (a calendar of the export account), or off with null,
    /// for the whole account: its tasks then go back to "AB Reminders" and the events the app made in the picked calendar are deleted.
    /// Call <see cref="AllowPickedCalendarAsync"/> first: devices without the consent leave these tasks to the ones that have it.
    /// </summary>
    public Task SetPickedCalendarAsync(string? googleCalendarId)
    {
        return _account.SetPickedCalendarAsync(googleCalendarId);
    }

    /// <summary>Writes what changed now (a run already going runs again after it).</summary>
    public async Task SyncNowAsync()
    {
        if (_running)
        {
            _again = true;
            return;
        }

        _running = true;
        bool retry = false;
        try
        {
            retry = await RunAsync();
        }
        catch (Exception ex)
        {
            // Never a reason to stop the app: say so, wait and try again.
            Log("export stopped (" + ex.GetType().Name + ")");
            SetStatus("Adding reminders to Google Calendar stopped; it will try again.", NeedsConsent);
            retry = true;
        }
        finally
        {
            _running = false;
        }

        if (retry)
        {
            _retryWait = _retryWait is TimeSpan wait ? TimeSpan.FromTicks(Math.Min(MaxRetry.Ticks, wait.Ticks * 2)) : FirstRetry;
            _retryAt = _time.GetUtcNow() + _retryWait.Value;
            Schedule(_retryWait.Value);
        }
        else
        {
            _retryWait = null;
            _retryAt = DateTimeOffset.MinValue;
        }

        if (_again)
        {
            _again = false;
            Soon();
        }
    }

    /// <returns>True when it should try again later.</returns>
    private async Task<bool> RunAsync()
    {
        if (!_store.IsActive)
        {
            return false;
        }

        TaskReminderSettings settings = _settings.Current;
        List<TaskItem> tasks = _store.Lists.Tasks.Select(item => item.Task).ToList();
        Guid? self = _store.SelfId();
        TimeZoneInfo zone = _time.LocalTimeZone;

        CalendarSource? source = ExportSource();
        if (source is null)
        {
            bool wantsAny = tasks.Any(task => TaskCalendarMapping.Exports(task, self)) || _state.Records.Count > 0;
            SetStatus(wantsAny ? "Connect a Google account to add reminders to Google Calendar." : null, false);
            return false;
        }

        if (_state.SourceId != source.Id)
        {
            if (_state.SourceId is not null && _state.Records.Count > 0)
            {
                Log($"export account changed; {_state.Records.Count} event(s) stay in the old account");
            }

            _state = new TaskCalendarExportState(source.Id, null, new Dictionary<Guid, TaskCalendarRecord>());
            await SaveStateAsync();
        }

        IReadOnlySet<Guid> keptElsewhere = TaskCalendarMapping.KeptElsewhereIds(tasks, self, zone, settings.DateOnlyEvent);
        string? picked = _account.PickedCalendarId;
        bool pickedByOther = _account.PickedCalendarBy is Guid by && by != (self ?? Guid.Empty);
        if (picked is not null && pickedByOther && !SharesTaskLists())
        {
            // "Sync task lists" off: another device's pick isn't this PC's to follow; its tasks stay in AB Reminders here.
            picked = null;
        }

        IReadOnlyDictionary<Guid, TaskCalendarWanted> wanted = TaskCalendarMapping.Wanted(tasks, zone, self, picked, settings.DateOnlyEvent);
        IReadOnlyList<TaskCalendarAction> planned = TaskCalendarDiff.Plan(wanted, _state.Records, keptElsewhere);

        // Tasks now kept on other devices only: their events stay for those devices (the same ids); this PC only forgets them.
        if (await ForgetLocallyAsync(planned))
        {
            planned = planned.Where(action => action.Kind != TaskCalendarActionKind.Forget).ToList();
        }

        // Nothing to write: no call to Google at all (also for users who never turned export on).
        if (Pending(planned).Count == 0)
        {
            _appCalendarTrouble = 0;
            SetStatus(null, NeedsConsent);
            return false;
        }

        GoogleAccessToken access;
        try
        {
            access = await AccessAsync(source);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            Log("sign-in refused (" + (int)ex.StatusCode.GetValueOrDefault() + ")");
            SetStatus("Google ended the sign-in. Connect the account again in Settings › Connections.", false);
            return true;
        }

        if (!access.Has(GoogleCalendarClient.AppCreatedScope))
        {
            SetStatus(AppConsentText, true);
            return false;
        }

        string? problem = null;
        bool needsConsent = false;
        List<TaskCalendarAction> actions = Pending(planned);
        if (!access.Has(GoogleCalendarClient.EventsScope))
        {
            // Without that consent nothing is written outside the app's own calendar.
            HashSet<Guid> waiting = actions.Where(TouchesPicked).Select(action => action.TaskId).ToHashSet();
            // A pick only taken over at the upgrade (adopted stamp) says nothing about that device's consent: treated as picked here.
            bool pickedElsewhere = picked is not null && pickedByOther && _account.PickedCalendarChosen;
            if (waiting.Count > 0 && pickedElsewhere)
            {
                // The account's pick was made on another device, which had the consent and writes these tasks there (the event ids are
                // shared). This PC adds none of them anywhere. It deletes its own "AB Reminders" event for such a task only once a read
                // shows the same event in the picked calendar, so a task never ends up with no event when that device doesn't write it
                // (lost consent, uninstalled, stopped syncing); its records in the picked calendar are kept for clean-up once consent is back.
                var confirmed = new List<TaskCalendarAction>();
                foreach (TaskCalendarAction action in actions.Where(action => waiting.Contains(action.TaskId) && IsAppCalendarDelete(action)))
                {
                    if (await _writer.EventExistsAsync(access.Token, picked!, action.Recorded!.EventId, CancellationToken.None))
                    {
                        confirmed.Add(action);
                    }
                }

                actions = actions.Where(action => !waiting.Contains(action.TaskId)).Concat(confirmed).ToList();
                problem = PickedElsewhereText;
                if (waiting.Count != _leftElsewhereLogged)
                {
                    _leftElsewhereLogged = waiting.Count;
                    Log($"picked calendar chosen on another device, no consent here: {waiting.Count} task(s) left to the devices that have it");
                }
            }
            else if (waiting.Count > 0)
            {
                // Picked here: a task whose event is (or would go) in the picked calendar waits as it is, its record kept, so the app's
                // events there can be cleaned up once consent is back.
                actions = actions.Where(action => !waiting.Contains(action.TaskId)).ToList();
                needsConsent = true;
                problem = PickedConsentText;
            }
        }

        var records = new Dictionary<Guid, TaskCalendarRecord>(_state.Records);
        int created = 0, updated = 0, deleted = 0, dropped = 0;
        bool retry = false;
        try
        {
            foreach (TaskCalendarAction action in actions)
            {
                switch (await WriteAsync(access.Token, action, records))
                {
                    case WriteResult.Done:
                        created += action.Kind == TaskCalendarActionKind.Create ? 1 : 0;
                        updated += action.Kind == TaskCalendarActionKind.Update ? 1 : 0;
                        deleted += action.Kind == TaskCalendarActionKind.Delete ? 1 : 0;
                        dropped += action.Wanted?.Event.Dropped ?? 0;
                        break;
                    case WriteResult.AppCalendarUnusable:
                        // Deleted by the user (its events went with it) or not this client's: forget it and its records and make a new
                        // one, at once the first time, then once per backoff wait.
                        foreach (Guid id in records.Where(pair => pair.Value.CalendarKey == TaskCalendarMapping.AppCalendarKey).Select(pair => pair.Key).ToList())
                        {
                            records.Remove(id);
                        }

                        _state = _state with { AppCalendarId = null };
                        if (_appCalendarTrouble == 0)
                        {
                            _again = true;
                        }
                        else
                        {
                            problem = "AB Reminders couldn't be used; the app will make it again and try later.";
                            retry = true;
                        }

                        _appCalendarTrouble++;
                        break;
                    case WriteResult.AppConsentMissing:
                        _access = null;
                        needsConsent = true;
                        problem = AppConsentText;
                        retry = true;
                        break;
                    case WriteResult.PickedConsentMissing:
                        _access = null;
                        needsConsent = true;
                        problem = PickedConsentText;
                        retry = true;
                        break;
                    case WriteResult.PickedCalendarRefused:
                        problem = "The picked calendar can't be changed by the app (missing, or not yours to edit).";
                        retry = true;
                        break;
                }

                if (_again || retry)
                {
                    break;
                }
            }
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                _access = null;
            }

            bool slowDown = GoogleErrorReason.IsRateLimit(ex);
            Log("write failed (" + (ex.StatusCode is { } code ? ((int)code).ToString(CultureInfo.InvariantCulture) : "no connection")
                + (slowDown ? ", rate limit" : string.Empty) + ")");
            problem ??= slowDown ? "Google asked the app to slow down; it will try again." : "Couldn't reach Google Calendar; it will try again.";
            retry = true;
        }
        catch (TaskCanceledException)
        {
            Log("write failed (timeout)");
            problem ??= "Couldn't reach Google Calendar; it will try again.";
            retry = true;
        }
        finally
        {
            _state = _state with { Records = records };
            await SaveStateAsync();
            if (created + updated + deleted > 0)
            {
                Log($"created {created}, updated {updated}, deleted {deleted}" + (dropped > 0 ? $"; {dropped} reminder(s) an all-day event can't carry" : string.Empty));
            }
        }

        if (!retry && !_again)
        {
            _appCalendarTrouble = 0;
        }

        SetStatus(problem, needsConsent);
        return retry;
    }

    private async Task<WriteResult> WriteAsync(string token, TaskCalendarAction action, Dictionary<Guid, TaskCalendarRecord> records)
    {
        CancellationToken none = CancellationToken.None;
        try
        {
            switch (action.Kind)
            {
                case TaskCalendarActionKind.Forget:
                    records.Remove(action.TaskId);
                    return WriteResult.Done;

                case TaskCalendarActionKind.Delete:
                    TaskCalendarRecord gone = action.Recorded!;
                    await _writer.DeleteEventAsync(token, gone.CalendarId, gone.EventId, none);
                    records.Remove(action.TaskId);
                    return WriteResult.Done;

                case TaskCalendarActionKind.Update:
                    TaskCalendarRecord known = action.Recorded!;
                    await _writer.PutEventAsync(token, known.CalendarId, known.EventId, action.Wanted!.Event, isNew: false, none);
                    records[action.TaskId] = known with { Signature = action.Wanted.Signature };
                    return WriteResult.Done;

                default:
                    TaskCalendarWanted want = action.Wanted!;
                    string calendarId = want.CalendarKey == TaskCalendarMapping.AppCalendarKey ? await AppCalendarIdAsync(token) : want.CalendarKey;
                    string eventId = TaskCalendarMapping.EventId(action.TaskId);
                    await _writer.PutEventAsync(token, calendarId, eventId, want.Event, isNew: true, none);
                    records[action.TaskId] = new TaskCalendarRecord(want.CalendarKey, calendarId, eventId, want.Signature);
                    return WriteResult.Done;
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.BadRequest && action.Wanted is { } refused)
        {
            Log("an event was refused (400); not tried again until the task changes");
            _refused.Add((action.TaskId, refused.Signature));
            return WriteResult.Done;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden && !GoogleErrorReason.IsRateLimit(ex))
        {
            // A rate limit falls through to the run's backoff.
            bool app = (action.Recorded?.CalendarKey ?? action.Wanted!.CalendarKey) == TaskCalendarMapping.AppCalendarKey;
            string code = ((int)ex.StatusCode.GetValueOrDefault()).ToString(CultureInfo.InvariantCulture);
            string where = app ? "AB Reminders" : "picked calendar";
            if (GoogleErrorReason.IsMissingScope(ex))
            {
                Log(where + " write needs consent (" + code + ")");
                return app ? WriteResult.AppConsentMissing : WriteResult.PickedConsentMissing;
            }

            Log(where + " refused (" + code + ")");
            return app ? WriteResult.AppCalendarUnusable : WriteResult.PickedCalendarRefused;
        }
    }

    private async Task<string> AppCalendarIdAsync(string token)
    {
        if (_state.AppCalendarId is { } known)
        {
            return known;
        }

        // One with the name is reused only once Google confirms this client made it; otherwise a new one is made.
        string? id = null;
        foreach (string candidate in await _writer.FindOwnedCalendarsAsync(token, AppCalendarName, CancellationToken.None))
        {
            if (await _writer.ClaimCalendarAsync(token, candidate, CancellationToken.None))
            {
                id = candidate;
                Log("AB Reminders found and confirmed");
                break;
            }
        }

        if (id is null)
        {
            id = await _writer.CreateCalendarAsync(token, AppCalendarName, CancellationToken.None);
            Log("AB Reminders made");
        }

        _state = _state with { AppCalendarId = id };
        await SaveStateAsync();
        return id;
    }

    private async Task<string?> AllowAsync(bool includeEvents, Func<Uri, bool> openBrowser, CancellationToken cancellationToken)
    {
        CalendarSource? source = ExportSource();
        if (source is null)
        {
            return "Connect a Google account first (Settings › Connections).";
        }

        if (!TryGetClient(out string clientId, out string clientSecret))
        {
            return "Enter your Google client ID and secret in Settings first.";
        }

        var scopes = new List<string> { GoogleCalendarClient.Scope, GoogleCalendarClient.AppCreatedScope };
        if (includeEvents)
        {
            scopes.Add(GoogleCalendarClient.EventsScope);
        }

        string scope = string.Join(' ', scopes);
        try
        {
            GoogleTokens tokens = await _google.SignInAsync(clientId, clientSecret, openBrowser, cancellationToken, scope, includeGrantedScopes: true);
            IReadOnlyList<GoogleCalendarEntry> calendars = await _google.ListCalendarsAsync(tokens.AccessToken, cancellationToken);
            string account = calendars.FirstOrDefault(calendar => calendar.IsPrimary)?.Id ?? string.Empty;
            if (!string.Equals(source.Name, $"Google · {account}", StringComparison.OrdinalIgnoreCase))
            {
                return $"That was another Google account. Sign in with the connected one ({source.Name}).";
            }

            var granted = new GoogleAccessToken(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.Scopes ?? string.Empty);
            if (!granted.Has(GoogleCalendarClient.AppCreatedScope))
            {
                return "Google didn't allow it: on Google's page, tick the box for calendars the app creates.";
            }

            if (includeEvents && !granted.Has(GoogleCalendarClient.EventsScope))
            {
                return "Google didn't allow changing events in your calendars: tick that box on Google's page.";
            }

            // The account may have been disconnected while Google's page was open: never bring it back.
            CalendarSource? current = _calendars.Sources.FirstOrDefault(candidate => candidate.Id == source.Id);
            if (current is null)
            {
                return "The Google account was disconnected meanwhile. Connect it again first.";
            }

            // The new sign-in holds the earlier scopes too (incremental consent), so reading carries on with it.
            _calendars.SaveSource(current with { ProtectedSecret = Convert.ToBase64String(_protector.Protect(tokens.RefreshToken)) });
            _access = granted;
            _accessSourceId = source.Id;
            Log(includeEvents ? "allowed: app calendar and picked calendar" : "allowed: app calendar");
            SetStatus(null, false);
            _retryWait = null;
            _retryAt = DateTimeOffset.MinValue;
            Soon();
            return null;
        }
        catch (GoogleSignInException ex)
        {
            return ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return "Couldn't reach Google. Try again in a moment.";
        }
    }

    private async Task<GoogleAccessToken> AccessAsync(CalendarSource source)
    {
        if (_access is { } cached && _accessSourceId == source.Id && cached.ExpiresAt > _time.GetUtcNow())
        {
            return cached;
        }

        string refreshToken = Unprotect(source.ProtectedSecret)
                              ?? throw new InvalidOperationException("The Google sign-in can't be read on this computer.");
        if (!TryGetClient(out string clientId, out string clientSecret))
        {
            throw new InvalidOperationException("The Google client ID and secret are missing in Settings.");
        }

        GoogleAccessToken access = await _google.RefreshWithScopesAsync(clientId, clientSecret, refreshToken, CancellationToken.None);
        _access = access;
        _accessSourceId = source.Id;
        return access;
    }

    // The account the events go to: the one used before while it is connected, else the first Google account.
    private CalendarSource? ExportSource()
    {
        List<CalendarSource> google = _calendars.Sources.Where(source => source.Kind == CalendarSourceKind.Google).ToList();
        return google.FirstOrDefault(source => source.Id == _state.SourceId) ?? google.FirstOrDefault();
    }

    private async Task<bool> ForgetLocallyAsync(IReadOnlyList<TaskCalendarAction> planned)
    {
        List<Guid> forgotten = planned.Where(action => action.Kind == TaskCalendarActionKind.Forget).Select(action => action.TaskId).ToList();
        if (forgotten.Count == 0)
        {
            return false;
        }

        var records = new Dictionary<Guid, TaskCalendarRecord>(_state.Records);
        forgotten.ForEach(id => records.Remove(id));
        _state = _state with { Records = records };
        await SaveStateAsync();
        Log($"{forgotten.Count} event(s) left to the devices that keep their tasks");
        return true;
    }

    private static bool IsAppCalendarDelete(TaskCalendarAction action)
    {
        return action is { Kind: TaskCalendarActionKind.Delete, Recorded.CalendarKey: TaskCalendarMapping.AppCalendarKey };
    }

    private static bool TouchesPicked(TaskCalendarAction action)
    {
        return (action.Recorded is { } recorded && recorded.CalendarKey != TaskCalendarMapping.AppCalendarKey)
               || (action.Wanted is { } wanted && wanted.CalendarKey != TaskCalendarMapping.AppCalendarKey);
    }

    private List<TaskCalendarAction> Pending(IReadOnlyList<TaskCalendarAction> actions)
    {
        return actions.Where(action => action.Wanted is not { } want || !_refused.Contains((action.TaskId, want.Signature))).ToList();
    }

    private void Soon()
    {
        DateTimeOffset now = _time.GetUtcNow();
        Schedule(_retryAt > now ? _retryAt - now : Debounce);
    }

    private void Schedule(TimeSpan wait)
    {
        if (_timer is null)
        {
            return;
        }

        _timer.Stop();
        _timer.Interval = wait < Debounce ? Debounce : wait;
        _timer.Start();
    }

    private void SetStatus(string? problem, bool needsConsent)
    {
        if (problem == Problem && needsConsent == NeedsConsent)
        {
            return;
        }

        Problem = problem;
        NeedsConsent = needsConsent;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task SaveStateAsync()
    {
        try
        {
            await _documents.SaveAsync(StateKey, _state);
        }
        catch (SqliteException)
        {
            // Kept in memory; the fixed event ids mean a lost record never makes a second event.
        }
    }

    private bool TryGetClient(out string clientId, out string clientSecret)
    {
        clientId = _calendars.Settings.GoogleClientId ?? string.Empty;
        clientSecret = Unprotect(_calendars.Settings.ProtectedGoogleClientSecret) ?? string.Empty;
        return clientId.Length > 0 && clientSecret.Length > 0;
    }

    private string? Unprotect(string? protectedSecret)
    {
        if (string.IsNullOrEmpty(protectedSecret))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(Convert.FromBase64String(protectedSecret));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private void Log(string line)
    {
        if (_logPath is not null)
        {
            try
            {
                File.AppendAllText(_logPath, _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture) + "  " + line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The log is a help, never a reason to stop.
            }
        }

        System.Diagnostics.Debug.WriteLine("tasks-calendar: " + line);
    }

    private enum WriteResult
    {
        Done,
        AppCalendarUnusable,
        AppConsentMissing,
        PickedConsentMissing,
        PickedCalendarRefused,
    }
}
