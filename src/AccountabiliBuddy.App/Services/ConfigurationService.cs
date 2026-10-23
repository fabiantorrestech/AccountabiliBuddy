using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Single owner of the in-memory <see cref="ProfileDocument"/>. Every change goes through
/// <see cref="UpdateAsync"/>, which notifies listeners and persists the result.
/// <para>
/// Stricter changes take effect at once; looser ones wait (<see cref="ConfigurationChangeGuard"/>).
/// <see cref="Current"/> is what is enforced; <see cref="Pending"/> holds the last saved configuration while its looser
/// parts count down (documents key "config.pending", so restarting the app doesn't skip the wait), and
/// <see cref="Editing"/> is what the editors show. Pending changes never take effect during a session.
/// </para>
/// Use from the UI thread only.
/// </summary>
public sealed class ConfigurationService
{
    private const string PendingDocumentKey = "config.pending";

    private readonly IProfileStore _store;
    private readonly SqliteJsonDocumentStore _documents;
    private readonly TimeProvider _timeProvider;

    // Saves of the pending changes run one after another so an older state never overwrites a newer one.
    private Task _pendingSave = Task.CompletedTask;

    public ConfigurationService(IProfileStore store, SqliteJsonDocumentStore documents, TimeProvider timeProvider)
    {
        _store = store;
        _documents = documents;
        _timeProvider = timeProvider;
    }

    /// <summary>The configuration in effect.</summary>
    public ProfileDocument Current { get; private set; } = ProfileDocument.CreateDefault();

    /// <summary>Looser changes still counting down; null when there are none.</summary>
    public PendingChanges? Pending { get; private set; }

    /// <summary>What the editors show and change: the pending configuration if there is one, otherwise the current one.</summary>
    public ProfileDocument Editing => Pending?.Document ?? Current;

    /// <summary>
    /// Says whether a day plan change has to wait (ADR 28: it frees some of today's work hours while the work-hours guard
    /// is on): a line describing it, or null when it applies at once like any other day plan change (ADR 18).
    /// </summary>
    public Func<DayPlan, DayPlan, string?>? DayPlanHold { get; set; }

    /// <summary>
    /// When a day plan change is waiting (<see cref="DayPlanHold"/>), what to tell the user who made it; null otherwise.
    /// </summary>
    public string? DayPlanWaitText => Pending is { } pending && !SameDayPlan(Current.DayPlan, pending.Document.DayPlan)
        ? $"Waits {(int)Current.Locks.LooseningDelay.TotalMinutes} min: it frees part of today's work hours (work hours on this computer). The banner counts down and can cancel it."
        : null;

    /// <summary>Set when saved settings could not be read. Saving is then disabled so nothing is overwritten.</summary>
    public string? LoadError { get; private set; }

    public bool IsReadOnly => LoadError is not null;

    /// <summary>Raised with <see cref="Current"/> whenever it changes.</summary>
    public event EventHandler<ProfileDocument>? Changed;

    /// <summary>Raised whenever <see cref="Pending"/> is set, replaced, applied or cancelled.</summary>
    public event EventHandler? PendingChanged;

    /// <summary>Raised when the user cancels the pending changes: editors should show <see cref="Current"/> again.</summary>
    public event EventHandler? PendingCancelled;

    /// <summary>
    /// Loads synchronously during startup, before any window exists.
    /// Runs on the thread pool so blocking here cannot deadlock the UI thread.
    /// </summary>
    public void LoadBlocking()
    {
        try
        {
            Current = Task.Run(() => _store.LoadAsync()).GetAwaiter().GetResult();
        }
        catch (UnsupportedSchemaVersionException ex)
        {
            LoadError = $"{ex.Message} Changes will not be saved.";
        }
        catch (JsonException ex)
        {
            LoadError = $"Saved settings could not be read ({ex.Message}). Changes will not be saved.";
        }
        catch (SqliteException ex)
        {
            LoadError = $"The settings database could not be opened ({ex.Message}). Changes will not be saved.";
        }

        if (!IsReadOnly)
        {
            LoadPendingBlocking();
        }
    }

    /// <summary>
    /// Changes the configuration the editors show. Stricter parts take effect now; looser parts wait, and any
    /// looser change than what was already waiting restarts the countdown.
    /// </summary>
    /// <param name="fromAnotherDevice">A change received from another device (ADR 22): the profiles it deletes wait
    /// like looser changes too. Deletions already waiting keep waiting through any change; others take effect at once.</param>
    /// <returns>The changes still waiting, or null when everything took effect.</returns>
    public async Task<PendingChanges?> UpdateAsync(Func<ProfileDocument, ProfileDocument> change, bool fromAnotherDevice = false)
    {
        ProfileDocument proposed = change(Editing);
        // A deletion waiting here is missing from the editors already, so an edit made meanwhile doesn't confirm it.
        HashSet<Guid> deletionsThatWait = Current.Profiles
            .Where(profile => fromAnotherDevice || Editing.Profiles.All(editing => editing.Id != profile.Id))
            .Select(profile => profile.Id)
            .ToHashSet();
        ConfigurationSplit split = HoldDayPlan(ConfigurationChangeGuard.Split(Current, proposed, deletionsThatWait), proposed);

        PendingChanges? next = null;
        if (split.HasWaitingChanges)
        {
            PendingChanges? previous = Pending;
            bool looserThanBefore = previous is null
                || LooseningDetector.Find(previous.Document, proposed).Count > 0
                || DayPlanHold?.Invoke(previous.Document.DayPlan, proposed.DayPlan) is not null
                || (fromAnotherDevice && ConfigurationChangeGuard.DeletionsWaiting(previous.Document, proposed).Count > 0);
            DateTimeOffset readyAt = looserThanBefore
                ? _timeProvider.GetUtcNow() + Current.Locks.LooseningDelay
                : previous!.ReadyAt;
            next = new PendingChanges(proposed, readyAt, split.WaitingChanges);
        }

        SetPending(next);
        await SetCurrentAsync(split.ApplyNow);
        return next;
    }

    /// <summary>
    /// Appends rules to one profile, skipping any that duplicate an existing rule (same target, kind and pattern).
    /// Block rules take effect at once; Allow rules wait like any looser change.
    /// </summary>
    /// <returns>How many rules were added.</returns>
    public async Task<int> AddRulesAsync(Guid profileId, IEnumerable<Rule> rules)
    {
        List<Rule> list = rules.ToList();
        int added = CountNewRules(Editing, profileId, list);
        if (added > 0)
        {
            await UpdateAsync(document => WithRules(document, profileId, list));
        }

        return added;
    }

    /// <summary>
    /// Adds a rule to several profiles right away, without the wait: the classify prompt's "add exemption", where the
    /// typed reason is the friction. Each profile gets its own copy (own id); duplicates are skipped.
    /// </summary>
    public async Task AddRuleNowAsync(IReadOnlyCollection<Guid> profileIds, Rule rule)
    {
        ProfileDocument Apply(ProfileDocument document)
        {
            foreach (Guid profileId in profileIds)
            {
                document = WithRules(document, profileId, new[] { rule with { Id = Guid.NewGuid() } });
            }

            return document;
        }

        if (Pending is { } pending)
        {
            SetPending(Recheck(pending with { Document = Apply(pending.Document) }, Apply(Current)));
        }

        await SetCurrentAsync(Apply(Current));
    }

    /// <summary>Drops the pending changes; the editors go back to the configuration in effect.</summary>
    public void CancelPending()
    {
        if (Pending is null)
        {
            return;
        }

        SetPending(null);
        PendingCancelled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Applies the pending changes once their countdown is over and no session is running.</summary>
    public async Task ApplyPendingIfDueAsync(bool sessionRunning)
    {
        if (Pending is not { } pending || sessionRunning || _timeProvider.GetUtcNow() < pending.ReadyAt)
        {
            return;
        }

        SetPending(null);
        await SetCurrentAsync(pending.Document);
    }

    private async Task SetCurrentAsync(ProfileDocument document)
    {
        Current = document;
        Changed?.Invoke(this, Current);

        if (!IsReadOnly)
        {
            await _store.SaveAsync(Current);
        }
    }

    private void LoadPendingBlocking()
    {
        try
        {
            SavedPendingChanges? saved = Task.Run(() => _documents.LoadAsync<SavedPendingChanges>(PendingDocumentKey))
                .GetAwaiter().GetResult();
            if (saved is not null)
            {
                ProfileDocument document = ProfileDocumentSerializer.Deserialize(saved.Document);
                Pending = Recheck(new PendingChanges(document, saved.ReadyAt, Array.Empty<string>()), Current);
            }
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or UnsupportedSchemaVersionException)
        {
            // Unreadable pending changes are dropped: the stricter configuration in effect stays.
            Pending = null;
        }
    }

    /// <summary>
    /// Re-describes what is still looser than <paramref name="current"/> (or deleted on another device, or a held day plan
    /// change); null when nothing is.
    /// </summary>
    private PendingChanges? Recheck(PendingChanges pending, ProfileDocument current)
    {
        List<string> changes =
        [
            .. LooseningDetector.Find(current, pending.Document),
            .. ConfigurationChangeGuard.DeletionsWaiting(current, pending.Document),
        ];
        if (!SameDayPlan(current.DayPlan, pending.Document.DayPlan))
        {
            // Only a held change leaves the plans different; it keeps waiting (never dropped), also before the guard loads.
            changes.Add(DayPlanHold?.Invoke(current.DayPlan, pending.Document.DayPlan) ?? "Day plan: a change waiting");
        }

        return changes.Count == 0 ? null : pending with { Changes = changes };
    }

    /// <summary>A day plan change that <see cref="DayPlanHold"/> holds keeps the current plan for now and waits.</summary>
    private ConfigurationSplit HoldDayPlan(ConfigurationSplit split, ProfileDocument proposed)
    {
        if (DayPlanHold?.Invoke(Current.DayPlan, proposed.DayPlan) is not string line)
        {
            return split;
        }

        return new ConfigurationSplit(split.ApplyNow with { DayPlan = Current.DayPlan }, [.. split.WaitingChanges, line]);
    }

    private static bool SameDayPlan(DayPlan a, DayPlan b)
    {
        return ReferenceEquals(a, b) || JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);
    }

    private void SetPending(PendingChanges? pending)
    {
        Pending = pending;
        PendingChanged?.Invoke(this, EventArgs.Empty);

        if (IsReadOnly)
        {
            return;
        }

        SavedPendingChanges? saved = pending is null
            ? null
            : new SavedPendingChanges(ProfileDocumentSerializer.Serialize(pending.Document), pending.ReadyAt);
        _pendingSave = _pendingSave.ContinueWith(
            async _ =>
            {
                try
                {
                    // Stored as JSON null when nothing is pending.
                    await _documents.SaveAsync(PendingDocumentKey, saved);
                }
                catch (SqliteException)
                {
                    // Best effort; the in-memory countdown still applies until the app closes.
                }
            },
            TaskScheduler.Default).Unwrap();
    }

    private static int CountNewRules(ProfileDocument document, Guid profileId, IEnumerable<Rule> rules)
    {
        FocusProfile? profile = document.Profiles.FirstOrDefault(candidate => candidate.Id == profileId);
        if (profile is null)
        {
            return 0;
        }

        var existing = new HashSet<string>(profile.Rules.Select(RuleKey), StringComparer.OrdinalIgnoreCase);
        return rules.Count(rule => existing.Add(RuleKey(rule)));
    }

    private static ProfileDocument WithRules(ProfileDocument document, Guid profileId, IEnumerable<Rule> rules)
    {
        return document with
        {
            Profiles = document.Profiles
                .Select(profile =>
                {
                    if (profile.Id != profileId)
                    {
                        return profile;
                    }

                    var existing = new HashSet<string>(profile.Rules.Select(RuleKey), StringComparer.OrdinalIgnoreCase);
                    List<Rule> added = rules.Where(rule => existing.Add(RuleKey(rule))).ToList();
                    return added.Count == 0 ? profile : profile with { Rules = profile.Rules.Concat(added).ToList() };
                })
                .ToList(),
        };
    }

    private static string RuleKey(Rule rule)
    {
        return $"{rule.Target}|{rule.Kind}|{rule.Pattern.Trim()}";
    }
}
