using System.Diagnostics;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// DIAG (slow tick on the Tasks page, remove once fixed): how long each step of a tick takes, one line per step, in Debug builds
/// only (a Release build writes nothing). Times and step names only, never a title or any text. Log: "tasks-tick-diag.log" in the
/// dev data folder.
/// </summary>
public static class TickDiag
{
#if DEBUG
    private static readonly string LogPath = Path.Combine(
        Environment.GetEnvironmentVariable("ACCOUNTABILIBUDDY_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AccountabiliBuddy-Dev"),
        "tasks-tick-diag.log");

    private static readonly object Gate = new();
#endif

    /// <summary>Times the step until the result is disposed: <c>using var _ = TickDiag.Measure("rebuild");</c>.</summary>
    public static IDisposable Measure(string step)
    {
#if DEBUG
        return new Scope(step);
#else
        return NoScope.Instance;
#endif
    }

    // Debug builds: how often each counted step ran (the render harness's tasks-perf-check reads it; a Release build counts nothing).
    private static readonly Dictionary<string, int> Counted = new(StringComparer.Ordinal);

    /// <summary>Counts one run of <paramref name="what"/> (Debug builds only; no file write, so it costs nothing that shows).</summary>
    [Conditional("DEBUG")]
    public static void Count(string what)
    {
        lock (Counted)
        {
            Counted[what] = Counted.GetValueOrDefault(what) + 1;
        }
    }

    /// <summary>A copy of the counts so far (empty in a Release build).</summary>
    public static IReadOnlyDictionary<string, int> Counts()
    {
        lock (Counted)
        {
            return new Dictionary<string, int>(Counted, StringComparer.Ordinal);
        }
    }

    /// <summary>A marker line (e.g. "tick start"), so the steps of one tick can be read together.</summary>
    [Conditional("DEBUG")]
    public static void Mark(string what)
    {
#if DEBUG
        Write($"---- {what}");
#endif
    }

#if DEBUG
    private static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // A diagnostic never breaks the app.
        }
    }

    private sealed class Scope : IDisposable
    {
        private readonly string _step;
        private readonly Stopwatch _watch = Stopwatch.StartNew();

        public Scope(string step)
        {
            _step = step;
        }

        public void Dispose()
        {
            Write($"{_step}: {_watch.Elapsed.TotalMilliseconds:F1} ms (thread {Environment.CurrentManagedThreadId})");
        }
    }
#else
    private sealed class NoScope : IDisposable
    {
        public static readonly NoScope Instance = new();

        public void Dispose()
        {
        }
    }
#endif
}
