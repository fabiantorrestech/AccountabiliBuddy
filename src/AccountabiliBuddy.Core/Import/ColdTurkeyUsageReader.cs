using AccountabiliBuddy.Core.Domain;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.Core.Import;

/// <summary>
/// Reads Cold Turkey's usage statistics: data-browser.db (table stats: site with path per minute) and
/// data-helper.db (table statsApp: app path per minute). Each row is one minute: date = Unix time of the minute,
/// seconds = time spent in it. Read from copies; Cold Turkey keeps the originals open.
/// </summary>
public static class ColdTurkeyUsageReader
{
    /// <summary>Process names that are part of Windows or are browsers, so never suggested as distractions.</summary>
    private static readonly HashSet<string> NotSuggestedApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "searchhost", "searchapp", "shellexperiencehost", "startmenuexperiencehost", "applicationframehost",
        "lockapp", "textinputhost", "systemsettings", "taskmgr", "windowsterminal", "cmd", "powershell", "pwsh",
        "chrome", "msedge", "brave", "firefox", "opera", "vivaldi", "cold turkey blocker", "accountabilibuddy.app",
    };

    /// <summary>The sites with the most time since <paramref name="since"/>, by host.</summary>
    public static IReadOnlyList<ImportedUsage> TopSites(string browserDatabasePath, DateTimeOffset since, int count)
    {
        return Totals(ReadSiteMinutes(browserDatabasePath, since), isApp: false, count);
    }

    /// <summary>The apps with the most time since <paramref name="since"/>, without Windows parts and browsers.</summary>
    public static IReadOnlyList<ImportedUsage> TopApps(string helperDatabasePath, DateTimeOffset since, int count)
    {
        IEnumerable<(DateTimeOffset At, string Name, int Seconds)> minutes = ReadAppMinutes(helperDatabasePath, since)
            .Where(minute => !NotSuggestedApps.Contains(minute.Name));
        return Totals(minutes, isApp: true, count);
    }

    /// <summary>
    /// Usage since <paramref name="since"/> as History segments: consecutive minutes on the same site or app are
    /// joined. Sites get the process name "browser" (Cold Turkey doesn't record which one); titles are not kept.
    /// </summary>
    public static IReadOnlyList<ActivitySegment> ReadSegments(
        string? browserDatabasePath, string? helperDatabasePath, DateTimeOffset since)
    {
        var segments = new List<ActivitySegment>();
        if (browserDatabasePath is not null)
        {
            segments.AddRange(Join(ReadSiteMinutes(browserDatabasePath, since, withPath: true), isApp: false));
        }

        if (helperDatabasePath is not null)
        {
            segments.AddRange(Join(ReadAppMinutes(helperDatabasePath, since), isApp: true));
        }

        return segments.OrderBy(segment => segment.Start).ToList();
    }

    private static IEnumerable<(DateTimeOffset At, string Name, int Seconds)> ReadSiteMinutes(
        string path, DateTimeOffset since, bool withPath = false)
    {
        foreach ((DateTimeOffset at, string site, int seconds) in ReadRows(path, "SELECT date, domain, seconds FROM stats WHERE date >= $since ORDER BY date;", since))
        {
            string name = withPath ? site : HostOf(site);
            if (name.Length > 0)
            {
                yield return (at, name, seconds);
            }
        }
    }

    private static IEnumerable<(DateTimeOffset At, string Name, int Seconds)> ReadAppMinutes(string path, DateTimeOffset since)
    {
        foreach ((DateTimeOffset at, string file, int seconds) in ReadRows(path, "SELECT date, file, seconds FROM statsApp WHERE date >= $since ORDER BY date;", since))
        {
            string name = Path.GetFileNameWithoutExtension(file.Replace('/', '\\').Split('\\')[^1]);
            if (name.Length > 0)
            {
                yield return (at, name, seconds);
            }
        }
    }

    private static List<(DateTimeOffset At, string Name, int Seconds)> ReadRows(string path, string sql, DateTimeOffset since)
    {
        var rows = new List<(DateTimeOffset, string, int)>();
        try
        {
            var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$since", since.ToUnixTimeSeconds());
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2))
                {
                    continue;
                }

                rows.Add((DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(0)), reader.GetString(1), reader.GetInt32(2)));
            }
        }
        catch (SqliteException ex)
        {
            throw new FormatException("Cold Turkey's usage statistics couldn't be read.", ex);
        }

        return rows;
    }

    private static IReadOnlyList<ImportedUsage> Totals(
        IEnumerable<(DateTimeOffset At, string Name, int Seconds)> minutes, bool isApp, int count)
    {
        return minutes
            .GroupBy(minute => minute.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ImportedUsage(group.Key, isApp, TimeSpan.FromSeconds(group.Sum(minute => minute.Seconds))))
            .OrderByDescending(usage => usage.Total)
            .Take(count)
            .ToList();
    }

    private static IEnumerable<ActivitySegment> Join(IEnumerable<(DateTimeOffset At, string Name, int Seconds)> minutes, bool isApp)
    {
        var open = new Dictionary<string, (DateTimeOffset Start, DateTimeOffset End)>(StringComparer.OrdinalIgnoreCase);
        foreach ((DateTimeOffset at, string name, int seconds) in minutes.OrderBy(minute => minute.At))
        {
            DateTimeOffset end = at + TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 60));
            if (open.TryGetValue(name, out var current) && at <= current.End + TimeSpan.FromMinutes(1))
            {
                open[name] = (current.Start, end > current.End ? end : current.End);
                continue;
            }

            if (open.Remove(name, out var finished))
            {
                yield return Segment(name, isApp, finished.Start, finished.End);
            }

            open[name] = (at, end);
        }

        foreach ((string name, (DateTimeOffset start, DateTimeOffset end)) in open)
        {
            yield return Segment(name, isApp, start, end);
        }
    }

    private static ActivitySegment Segment(string name, bool isApp, DateTimeOffset start, DateTimeOffset end)
    {
        return new ActivitySegment(
            start,
            end,
            MonitorId: "imported",
            ProcessName: isApp ? name : "browser",
            WindowTitle: string.Empty,
            Url: isApp ? null : "https://" + name,
            Classification.Unknown,
            ProfileId: null,
            InFocusSession: false);
    }

    private static string HostOf(string site)
    {
        string host = site.Split('/')[0].Trim().ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
}
