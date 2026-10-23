using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Promote repeats (redesign phase 2): a site or app answered "On track (this session)" in <c>threshold</c> different
/// sessions is offered once as a permanent exemption. Answers in one session count once.
/// </summary>
public static class RepeatAllows
{
    public const int DefaultThreshold = 3;

    /// <param name="sessions">Sessions covering the labels; one still running counts until <paramref name="now"/>.</param>
    /// <param name="alreadyOffered">Keys offered before (either answer): never offered again.</param>
    public static IReadOnlyList<RepeatAllow> Find(
        IReadOnlyList<SessionAllowLabel> labels,
        IReadOnlyList<SessionRecord> sessions,
        IReadOnlySet<string> alreadyOffered,
        DateTimeOffset now,
        int threshold = DefaultThreshold)
    {
        var sessionsByKey = new Dictionary<string, HashSet<DateTimeOffset>>(StringComparer.OrdinalIgnoreCase);
        var labelByKey = new Dictionary<string, SessionAllowLabel>(StringComparer.OrdinalIgnoreCase);
        foreach (SessionAllowLabel label in labels)
        {
            SessionRecord? session = sessions.FirstOrDefault(record => label.At >= record.StartedAt && label.At <= (record.EndedAt ?? now));
            if (session is null)
            {
                continue;
            }

            string key = (label.Domain ?? label.ProcessName).ToLowerInvariant();
            if (!sessionsByKey.TryGetValue(key, out HashSet<DateTimeOffset>? starts))
            {
                starts = [];
                sessionsByKey[key] = starts;
                labelByKey[key] = label;
            }

            starts.Add(session.StartedAt);
        }

        return sessionsByKey
            .Where(entry => entry.Value.Count >= threshold && !alreadyOffered.Contains(entry.Key))
            .Select(entry => ToOffer(entry.Key, labelByKey[entry.Key]))
            .OrderBy(offer => offer.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static RepeatAllow ToOffer(string key, SessionAllowLabel label)
    {
        Rule rule = label.Domain is not null
            ? new Rule(Guid.NewGuid(), MatchTarget.Url, MatchKind.Domain, label.Domain, RuleVerdict.Allow, "On track in three sessions")
            : new Rule(Guid.NewGuid(), MatchTarget.ProcessName, MatchKind.Exact, label.ProcessName, RuleVerdict.Allow, "On track in three sessions");
        return new RepeatAllow(key, label.Domain ?? label.ProcessName, rule);
    }
}
