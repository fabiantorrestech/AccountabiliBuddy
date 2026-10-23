using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The end-of-session review (redesign phase 2): when a session ends, the sites and apps it used for a minute or more with no
/// rule (at most <see cref="MaxRows"/>, ignored ones left out) become <see cref="Pending"/>, the card on Today. Its choices
/// write rules into the session's profile (allows also into the pre-ticked profiles). "New this week" in History builds the
/// same card over the last 7 days. Ignored sites and apps are kept in documents "review.ignored". Use from the UI thread only.
/// </summary>
public sealed class SessionReviewService : IDisposable
{
    public const int MaxRows = 8;
    private const string IgnoredKey = "review.ignored";
    private static readonly TimeSpan MinimumTime = TimeSpan.FromMinutes(1);

    private readonly SqliteActivityLog _log;
    private readonly ConfigurationService _configuration;
    private readonly SqliteJsonDocumentStore _store;
    private readonly SessionController _sessions;
    private readonly Func<IReadOnlySet<Guid>> _preTicked;
    private HashSet<string> _ignored = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _sessionStart;
    private Guid? _sessionProfile;
    private Task _building = Task.CompletedTask;

    /// <param name="preTicked">The profiles an allow also goes to (the classify prompt's pre-ticked ones).</param>
    public SessionReviewService(
        SqliteActivityLog log, ConfigurationService configuration, SqliteJsonDocumentStore store, SessionController sessions, Func<IReadOnlySet<Guid>> preTicked)
    {
        _log = log;
        _configuration = configuration;
        _store = store;
        _sessions = sessions;
        _preTicked = preTicked;
        _sessions.StateChanged += OnStateChanged;
    }

    /// <summary>The card waiting on Today, or null.</summary>
    public SessionReviewViewModel? Pending { get; private set; }

    /// <summary>Raised when <see cref="Pending"/> changes.</summary>
    public event EventHandler? Changed;

    public void LoadBlocking()
    {
        try
        {
            List<string>? saved = Task.Run(() => _store.LoadAsync<List<string>>(IgnoredKey)).GetAwaiter().GetResult();
            _ignored = new HashSet<string>(saved ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        if (_sessions.State.IsActive)
        {
            // A session resumed at launch: its review covers it from its phase's start.
            _sessionStart = _sessions.State.PhaseStartedAt;
            _sessionProfile = _sessions.State.ProfileId;
        }
    }

    /// <summary>Completes when the card for the session that just ended is ready (for the checks).</summary>
    public Task WhenReady() => _building;

    /// <summary>"New this week": the last 7 days' unknown time, written to the profile picked on the card.</summary>
    public async Task<SessionReviewViewModel?> BuildWeekAsync()
    {
        DateTimeOffset now = _sessions.Now;
        IReadOnlyList<FocusProfile> profiles = _configuration.Current.Profiles;
        if (profiles.Count == 0)
        {
            return null;
        }

        List<ReviewRowViewModel> rows = await RowsAsync(now.AddDays(-7), now);
        if (rows.Count == 0)
        {
            return null;
        }

        return new SessionReviewViewModel("New this week: sites and apps with no rule", rows, profiles[0].Id,
            profiles.Select(ProfileOption.From).ToList(), card => ApplyAsync(card, closesPending: false));
    }

    public void Dispose()
    {
        _sessions.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(object? sender, SessionState state)
    {
        if (state.IsActive && _sessionStart is null)
        {
            _sessionStart = _sessions.Now;
            _sessionProfile = state.ProfileId;
            SetPending(null); // A new session: last session's card is old news.
            return;
        }

        if (!state.IsActive && _sessionStart is DateTimeOffset start && _sessionProfile is Guid profileId)
        {
            _sessionStart = null;
            _sessionProfile = null;
            _building = BuildSessionAsync(start, _sessions.Now, profileId);
        }
    }

    private async Task BuildSessionAsync(DateTimeOffset start, DateTimeOffset end, Guid profileId)
    {
        List<ReviewRowViewModel> rows = await RowsAsync(start, end);
        SetPending(rows.Count == 0 ? null
            : new SessionReviewViewModel("Last session: what had no rule", rows, profileId, null, card => ApplyAsync(card, closesPending: true)));
    }

    private async Task<List<ReviewRowViewModel>> RowsAsync(DateTimeOffset from, DateTimeOffset to)
    {
        IReadOnlyList<AppSiteUsage> usage = await _log.GetUnknownUsageAsync(from, to);
        // Settled since (a rule for it in any profile) or ignored: not proposed again.
        var settled = _configuration.Current.Profiles
            .SelectMany(profile => profile.Rules)
            .Where(rule => rule.Target is MatchTarget.Url or MatchTarget.ProcessName)
            .Select(rule => rule.Pattern.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return usage
            .Where(entry => entry.Time >= MinimumTime && !_ignored.Contains(entry.Site ?? entry.App) && !settled.Contains(entry.Site ?? entry.App))
            .Take(MaxRows)
            .Select(entry => new ReviewRowViewModel(entry.Site, entry.App, entry.Time))
            .ToList();
    }

    private async Task ApplyAsync(SessionReviewViewModel card, bool closesPending)
    {
        string reason = card.Reason.Trim();
        foreach (ReviewRowViewModel row in card.Rows)
        {
            switch (row.Choice)
            {
                case ReviewChoice.Distracting:
                    // Stricter: at once.
                    await _configuration.AddRulesAsync(card.ProfileId, [row.RuleFor(RuleVerdict.Block)]);
                    break;
                case ReviewChoice.Fine:
                    // An exemption: the typed reason is the friction, as in the classify prompt.
                    Rule allow = row.RuleFor(RuleVerdict.Allow) with { Note = $"From the session review: {reason}" };
                    await _configuration.AddRuleNowAsync(_preTicked().Prepend(card.ProfileId).Distinct().ToList(), allow);
                    break;
                case ReviewChoice.Ignore:
                    _ignored.Add(row.Key);
                    break;
            }
        }

        await _store.SaveAsync(IgnoredKey, _ignored.Order(StringComparer.OrdinalIgnoreCase).ToList());
        if (closesPending)
        {
            SetPending(null);
        }
    }

    private void SetPending(SessionReviewViewModel? card)
    {
        if (card is not null)
        {
            card.Closed += (_, _) =>
            {
                if (Pending == card)
                {
                    SetPending(null);
                }
            };
        }

        Pending = card;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
