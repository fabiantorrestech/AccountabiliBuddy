namespace AccountabiliBuddy.Core.Goals;

/// <summary>
/// ADR 79 forward compatibility on every change to the goal lists: what this app doesn't know is never removed by it. A screen or a
/// rule that builds an item again (a new target, new extra tiles, a reminder from its editor row) leaves out the unknown fields its
/// stored copy had; <see cref="Keep(IReadOnlyList{Goal}, IReadOnlyList{Goal})"/> and its twins put them back from the copy before the
/// change, and a goal's newer source rules, a review's newer decisions and a list's newer items stay too. Matched by id (an item, a
/// source rule), by goal (a review decision), by first day (a pause), by kind and text (an app entry) and by place (a target, the
/// second target, a waiting change, the extra tiles). An object that has unknown fields (or newer items) of its own keeps those as
/// given. The phone's <c>GoalUnknowns</c> is the same rule (parity fixtures).
/// </summary>
public static class GoalUnknowns
{
    public static IReadOnlyList<GoalArea> Keep(IReadOnlyList<GoalArea> before, IReadOnlyList<GoalArea> after)
    {
        return Each(before, after, area => area.Id, Keep);
    }

    public static IReadOnlyList<Goal> Keep(IReadOnlyList<Goal> before, IReadOnlyList<Goal> after)
    {
        return Each(before, after, goal => goal.Id, Keep);
    }

    public static IReadOnlyList<GoalEntry> Keep(IReadOnlyList<GoalEntry> before, IReadOnlyList<GoalEntry> after)
    {
        return Each(before, after, entry => entry.Id, Keep);
    }

    public static IReadOnlyList<GoalReminder> Keep(IReadOnlyList<GoalReminder> before, IReadOnlyList<GoalReminder> after)
    {
        return Each(before, after, reminder => reminder.Id, Keep);
    }

    public static IReadOnlyList<GoalReviewRecord> Keep(IReadOnlyList<GoalReviewRecord> before, IReadOnlyList<GoalReviewRecord> after)
    {
        return Each(before, after, review => review.Id, Keep);
    }

    /// <summary>A list's newer items: the ones after the change, or, when it has none, the ones before (nothing here removes them).</summary>
    public static IReadOnlyList<GoalNewerItem> KeepNewer(IReadOnlyList<GoalNewerItem> before, IReadOnlyList<GoalNewerItem> after)
    {
        return after.Count == 0 ? before : after;
    }

    public static GoalArea Keep(GoalArea old, GoalArea now)
    {
        return now.Extra is null && old.Extra is not null ? now with { Extra = old.Extra } : now;
    }

    public static GoalEntry Keep(GoalEntry old, GoalEntry now)
    {
        return now.Extra is null && old.Extra is not null ? now with { Extra = old.Extra } : now;
    }

    public static GoalReminder Keep(GoalReminder old, GoalReminder now)
    {
        return now.Extra is null && old.Extra is not null ? now with { Extra = old.Extra } : now;
    }

    public static Goal Keep(Goal old, Goal now)
    {
        Goal kept = now;
        if (now.Extra is null && old.Extra is not null)
        {
            kept = kept with { Extra = old.Extra };
        }

        GoalTarget target = Keep(old.Target, now.Target);
        if (!ReferenceEquals(target, now.Target))
        {
            kept = kept with { Target = target };
        }

        if (old.SecondTarget is { } oldSecond && now.SecondTarget is { } nowSecond && Keep(oldSecond, nowSecond) is var second
            && !ReferenceEquals(second, nowSecond))
        {
            kept = kept with { SecondTarget = second };
        }

        if (old.ExtraTiles is { Extra: not null } oldTiles && now.ExtraTiles is { Extra: null } nowTiles)
        {
            kept = kept with { ExtraTiles = nowTiles with { Extra = oldTiles.Extra } };
        }

        if (now.Sources is { } nowSources && KeepSources(old.Sources ?? [], nowSources) is var sources && !ReferenceEquals(sources, nowSources))
        {
            kept = kept with { Sources = sources };
        }

        if (old.Pauses is { } oldPauses && now.Pauses is { } nowPauses)
        {
            IReadOnlyList<GoalPauseStretch> pauses = Each(oldPauses, nowPauses, pause => pause.From, (was, stretch) =>
                stretch.Extra is null && was.Extra is not null ? stretch with { Extra = was.Extra } : stretch);
            if (!ReferenceEquals(pauses, nowPauses))
            {
                kept = kept with { Pauses = pauses };
            }
        }

        return kept;
    }

    public static GoalReviewRecord Keep(GoalReviewRecord old, GoalReviewRecord now)
    {
        GoalReviewRecord kept = now.Extra is null && old.Extra is not null ? now with { Extra = old.Extra } : now;
        if (now.Decisions is not { } nowDecisions)
        {
            return kept;
        }

        IReadOnlyList<GoalReviewDecision> known = Each(old.Decisions ?? [], nowDecisions, decision => decision.GoalId, (was, decision) =>
            decision.Extra is null && was.Extra is not null ? decision with { Extra = was.Extra } : decision);
        IReadOnlyList<GoalNewerItem> oldNewer = GoalNewerJson.NewerOf(old.Decisions);
        IReadOnlyList<GoalNewerItem> nowNewer = GoalNewerJson.NewerOf(nowDecisions);
        bool newerBack = nowNewer.Count == 0 && oldNewer.Count > 0;
        if (ReferenceEquals(known, nowDecisions) && !newerBack)
        {
            return kept;
        }

        return kept with { Decisions = GoalNewerJson.WithNewer(known, newerBack ? oldNewer : nowNewer) };
    }

    public static GoalTarget Keep(GoalTarget old, GoalTarget now)
    {
        GoalTarget kept = now.Extra is null && old.Extra is not null ? now with { Extra = old.Extra } : now;
        if (old.PendingChange is { Extra: not null } oldPending && now.PendingChange is { Extra: null } nowPending)
        {
            kept = kept with { PendingChange = nowPending with { Extra = oldPending.Extra } };
        }

        return kept;
    }

    public static GoalSourceRule Keep(GoalSourceRule old, GoalSourceRule now)
    {
        GoalSourceRule kept = now.Extra is null && old.Extra is not null ? now with { Extra = old.Extra } : now;
        if (old.Apps is { } oldApps && now.Apps is { } nowApps)
        {
            IReadOnlyList<GoalAppMatch> apps = Each(oldApps, nowApps, app => (app.Kind, app.Text), (was, app) =>
                app.Extra is null && was.Extra is not null ? app with { Extra = was.Extra } : app);
            if (!ReferenceEquals(apps, nowApps))
            {
                kept = kept with { Apps = apps };
            }
        }

        return kept;
    }

    private static IReadOnlyList<GoalSourceRule> KeepSources(IReadOnlyList<GoalSourceRule> old, IReadOnlyList<GoalSourceRule> now)
    {
        IReadOnlyList<GoalSourceRule> known = Each(old, now, rule => rule.Id, Keep);
        IReadOnlyList<GoalNewerItem> oldNewer = GoalNewerJson.NewerOf(old);
        IReadOnlyList<GoalNewerItem> nowNewer = GoalNewerJson.NewerOf(now);
        bool newerBack = nowNewer.Count == 0 && oldNewer.Count > 0;
        return ReferenceEquals(known, now) && !newerBack ? now : GoalNewerJson.WithNewer(known, newerBack ? oldNewer : nowNewer);
    }

    // Each item of after with its old twin (the first of that key before) passed through keep; after itself when nothing changed.
    private static IReadOnlyList<T> Each<T, TKey>(IReadOnlyList<T> before, IReadOnlyList<T> after, Func<T, TKey> key, Func<T, T, T> keep)
        where T : class
        where TKey : notnull
    {
        var olds = new Dictionary<TKey, T>();
        foreach (T item in before)
        {
            olds.TryAdd(key(item), item);
        }

        var kept = new List<T>(after.Count);
        bool changed = false;
        foreach (T item in after)
        {
            T next = olds.TryGetValue(key(item), out T? old) ? keep(old, item) : item;
            changed |= !ReferenceEquals(next, item);
            kept.Add(next);
        }

        return changed ? GoalNewerJson.WithNewer(kept, GoalNewerJson.NewerOf(after)) : after;
    }
}
