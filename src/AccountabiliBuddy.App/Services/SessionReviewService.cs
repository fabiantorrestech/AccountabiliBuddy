using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
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
    private const string PromotedKey = "review.promoted";
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromDays(30);
    private static readonly TimeSpan MinimumTime = TimeSpan.FromMinutes(1);

    private readonly SqliteActivityLog _log;
    private readonly ConfigurationService _configuration;
    private readonly SqliteJsonDocumentStore _store;
    private readonly SessionController _sessions;
    private readonly Func<IReadOnlySet<Guid>> _preTicked;
    private HashSet<string> _ignored = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _promoted = new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>The notes kept between two times (redesign phase 5), shown on the session's card; null: none.</summary>
    public Func<DateTimeOffset, DateTimeOffset, IReadOnlyList<string>>? NotesBetween { get; init; }

    /// <summary>True when a phone counts pickups (ADR 26): then a card comes after every session, for the phone's line.</summary>
    public Func<bool>? PhoneCounting { get; init; }

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
            List<string>? offered = Task.Run(() => _store.LoadAsync<List<string>>(PromotedKey)).GetAwaiter().GetResult();
            _promoted = new HashSet<string>(offered ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            _ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _promoted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
        // A test session has no review card.
        if (state.IsTest)
        {
            return;
        }

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
        List<PromoteRowViewModel> promotions = await PromotionsAsync(end);
        IReadOnlyList<string> notes = NotesBetween?.Invoke(start, end) ?? [];
        bool phone = PhoneCounting?.Invoke() == true;
        string heading = rows.Count > 0 || promotions.Count > 0 ? "Last session: what had no rule"
            : notes.Count > 0 ? "Last session: your notes"
            : "Last session";
        SetPending(rows.Count == 0 && promotions.Count == 0 && notes.Count == 0 && !phone ? null
            : new SessionReviewViewModel(heading, rows, profileId, null, card => ApplyAsync(card, closesPending: true), promotions, notes)
            {
                SessionStart = start,
            });
    }

    /// <summary>Sites and apps called on track in three sessions of the last 30 days, not offered before.</summary>
    private async Task<List<PromoteRowViewModel>> PromotionsAsync(DateTimeOffset now)
    {
        IReadOnlyList<SessionAllowLabel> labels = await _log.GetSessionAllowsAsync(now - RepeatWindow, now);
        if (labels.Count == 0)
        {
            return [];
        }

        IReadOnlyList<SessionEvent> events = await _log.GetSessionEventsAsync(now - RepeatWindow - TimeSpan.FromDays(1), now);
        IReadOnlyList<SessionRecord> sessions = SessionHistoryBuilder.Build(events);
        return RepeatAllows.Find(labels, sessions, _promoted, now).Select(offer => new PromoteRowViewModel(offer)).ToList();
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

        foreach (PromoteRowViewModel promotion in card.Promotions.Where(promotion => promotion.IsYes))
        {
            Rule allow = promotion.Offer.SuggestedRule with { Id = Guid.NewGuid(), Note = $"On track in three sessions: {reason}" };
            await _configuration.AddRuleNowAsync(_preTicked().Prepend(card.ProfileId).Distinct().ToList(), allow);
        }

        await MarkOfferedAsync(card);
        await _store.SaveAsync(IgnoredKey, _ignored.Order(StringComparer.OrdinalIgnoreCase).ToList());
        if (closesPending)
        {
            SetPending(null);
        }
    }

    /// <summary>Offers are made once, whatever the answer (Skip included).</summary>
    private async Task MarkOfferedAsync(SessionReviewViewModel card)
    {
        if (card.Promotions.Count == 0)
        {
            return;
        }

        foreach (PromoteRowViewModel promotion in card.Promotions)
        {
            _promoted.Add(promotion.Offer.Key);
        }

        await _store.SaveAsync(PromotedKey, _promoted.Order(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private void SetPending(SessionReviewViewModel? card)
    {
        if (card is not null)
        {
            card.Skipped += (_, _) => _ = MarkOfferedAsync(card);
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
