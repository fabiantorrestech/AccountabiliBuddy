using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Waited ends confirmed early (ADR 66; documents key "armedEnds"): each applies itself when its wait is over, through its
/// <see cref="IArmedEndHandler"/>, and can be cancelled until then. Kept on disk with the reason DPAPI-sealed, so a restart
/// neither resets nor skips a wait. One per kind; arming a kind again replaces the older one. Use from the UI thread only.
/// </summary>
public sealed class ArmedEndService : IDisposable
{
    private const string DocumentKey = "armedEnds";
    private const string Unreadable = "(reason not readable)";

    private readonly SessionController _sessions;
    private readonly SqliteJsonDocumentStore _store;
    private readonly IDataProtector _protector;
    private readonly Action<string, string> _notify;
    private readonly Dictionary<ArmedEndKind, IArmedEndHandler> _handlers = [];
    private List<ArmedEnd> _ends = [];
    private bool _applying;

    // Saves run one after another so an older list never overwrites a newer one.
    private Task _pendingSave = Task.CompletedTask;

    public ArmedEndService(SessionController sessions, SqliteJsonDocumentStore store, IDataProtector protector, Action<string, string> notify)
    {
        _sessions = sessions;
        _store = store;
        _protector = protector;
        _notify = notify;
        _sessions.Ticked += OnTicked;
    }

    /// <summary>Raised when one is armed, cancelled, dropped or applied.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<ArmedEnd> Current => _ends;

    /// <summary>The one that applies first; null for none.</summary>
    public ArmedEnd? Next => ArmedEndRules.Next(_ends);

    public void Register(IArmedEndHandler handler)
    {
        _handlers[handler.Kind] = handler;
    }

    /// <summary>Loads the saved list. Call once the sessions have resumed and the handlers are registered.</summary>
    public void LoadBlocking()
    {
        try
        {
            _ends = Task.Run(() => _store.LoadAsync<List<ArmedEnd>>(DocumentKey)).GetAwaiter().GetResult() ?? [];
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _ends = [];
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Arms <paramref name="kind"/>: its wait of <paramref name="wait"/> counts from <paramref name="requestedAt"/>.</summary>
    /// <returns>Null when the wait is already over (the caller ends at once instead).</returns>
    /// <param name="unfinishedToQueued">An end of the session confirmed as "End and mark the rest unfinished" (the same wait).</param>
    public ArmedEnd? Arm(
        ArmedEndKind kind, DateTimeOffset requestedAt, TimeSpan wait, string reason, Guid? profileId = null, ArmedWorkEnd? workEnd = null,
        bool unfinishedToQueued = false)
    {
        string trimmed = reason.Trim();
        string sealedReason = trimmed.Length == 0 ? string.Empty : Convert.ToBase64String(_protector.Protect(trimmed));
        ArmedEnd? end = ArmedEndRules.Arm(kind, requestedAt, wait, _sessions.Now, sealedReason, profileId, workEnd);
        if (end is null)
        {
            return null;
        }

        end = end with { UnfinishedToQueued = unfinishedToQueued && kind == ArmedEndKind.EndSession };

        Save([.. _ends.Where(other => other.Kind != kind), end]);
        return end;
    }

    /// <summary>Cancels one still waiting (always allowed: it is the stricter direction).</summary>
    /// <returns>False when it is no longer armed (applied, dropped or cancelled already).</returns>
    public bool Cancel(Guid id)
    {
        if (_ends.All(end => end.Id != id))
        {
            return false;
        }

        Save([.. _ends.Where(end => end.Id != id)]);
        return true;
    }

    /// <summary>Drops any armed end of <paramref name="kind"/>, e.g. when the session ended some other way.</summary>
    public void Drop(ArmedEndKind kind)
    {
        if (_ends.Any(end => end.Kind == kind))
        {
            Save([.. _ends.Where(end => end.Kind != kind)]);
        }
    }

    public ArmedEnd? Find(ArmedEndKind kind) => _ends.FirstOrDefault(end => end.Kind == kind);

    /// <summary>"Session ends" for the countdown lines; the kind's name when no handler is registered.</summary>
    public string Label(ArmedEnd end) => _handlers.TryGetValue(end.Kind, out IArmedEndHandler? handler) ? handler.Label(end) : end.Kind.ToString();

    public TimeSpan Left(ArmedEnd end) => ArmedEndRules.Left(end, _sessions.Now);

    /// <summary>"Session ends in 0:42", for one line.</summary>
    public string Describe(ArmedEnd end) => $"{Label(end)} in {ArmedEndRules.Countdown(Left(end))}";

    public void Dispose()
    {
        _sessions.Ticked -= OnTicked;
    }

    private void OnTicked(object? sender, EventArgs e)
    {
        _ = AdvanceAsync();
    }

    private async Task AdvanceAsync()
    {
        if (_applying || _ends.Count == 0)
        {
            return;
        }

        // Gone: what it ends no longer exists.
        List<ArmedEnd> stillDue = _ends.Where(IsStillDue).ToList();
        if (stillDue.Count != _ends.Count)
        {
            Save(stillDue);
        }

        DateTimeOffset now = _sessions.Now;
        ArmedEnd? due = _ends.Where(end => ArmedEndRules.IsDue(end, now)).OrderBy(end => end.ReadyAt).FirstOrDefault();
        if (due is null || !_handlers.TryGetValue(due.Kind, out IArmedEndHandler? handler))
        {
            return;
        }

        // Taken off the list first, so it applies once even if applying raises another tick.
        _applying = true;
        try
        {
            Save([.. _ends.Where(end => end.Id != due.Id)]);
            string notice = await handler.ApplyAsync(due, Unseal(due.SealedReason));
            _notify(handler.DoneTitle(due), notice);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or InvalidOperationException)
        {
            // Never applied silently wrong: it is off the list, and the user is told to do it by hand.
            _notify("Couldn't finish it", $"{handler.Label(due)} didn't apply ({ex.Message}). Do it again by hand.");
        }
        finally
        {
            _applying = false;
        }
    }

    private bool IsStillDue(ArmedEnd end) => !_handlers.TryGetValue(end.Kind, out IArmedEndHandler? handler) || handler.IsStillDue(end);

    private string Unseal(string sealedReason)
    {
        if (sealedReason.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return _protector.Unprotect(Convert.FromBase64String(sealedReason)) ?? Unreadable;
        }
        catch (FormatException)
        {
            return Unreadable;
        }
    }

    private void Save(List<ArmedEnd> ends)
    {
        _ends = ends;
        Changed?.Invoke(this, EventArgs.Empty);
        _pendingSave = _pendingSave.ContinueWith(
            async _ =>
            {
                try
                {
                    await _store.SaveAsync(DocumentKey, ends);
                }
                catch (SqliteException)
                {
                    // Best effort; the list in memory still applies until the app closes.
                }
            },
            TaskScheduler.Default).Unwrap();
    }
}
