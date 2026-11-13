using System.Globalization;
using System.Text;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The rolling sync log (SYNC1b, ADR 86): one line per sync pass with the time, the method, the direction and counts (files, parts merged,
/// parts held, skipped, unreadable, milliseconds), device-local in <c>sync.log</c> beside <c>goals-history.log</c>. By construction it can't
/// hold private text: the only inputs are a method code from <see cref="SyncReceiptParts.Methods"/>, a <see cref="SyncLogDirection"/>, a code
/// from <see cref="Notes"/> and numbers, so never a title, note, name, file name, path, address, device name or id. Passes that changed
/// nothing are folded into one "quiet" line per method, direction and hour; lines are buffered and written together (CPU and battery); only
/// the last 7 days stay and the file never grows past <see cref="MaxBytes"/> (oldest lines go first). Thread-safe.
/// </summary>
public sealed class SyncLog
{
    /// <summary>How long a line is kept.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromDays(7);

    /// <summary>The most the file holds (512 KB).</summary>
    public const long MaxBytes = 512 * 1024;

    /// <summary>The fixed notes: a re-read, a send again, a history re-read, a history send again, a stale file removed, a file ignored, a file tried again.</summary>
    public static readonly IReadOnlyList<string> Notes = ["reread", "resend", "history-reread", "history-resend", "stale-removed", "ignored", "retry"];

    private static readonly TimeSpan FlushAfter = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TrimEvery = TimeSpan.FromHours(6);

    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly List<string> _buffer = [];
    private readonly Dictionary<(string Method, SyncLogDirection Direction), QuietRun> _quiet = [];
    private readonly object _lock = new();
    private bool _flushScheduled;
    private DateTimeOffset _lastTrim = DateTimeOffset.MinValue;

    public SyncLog(string path, TimeProvider time)
    {
        _path = path;
        _time = time;
    }

    /// <summary>
    /// One pass. A read or history pass that merged nothing, held nothing, skipped nothing and found nothing unreadable is "quiet": it is
    /// counted into the hour's one line instead of getting its own.
    /// </summary>
    public void Pass(string method, SyncLogDirection direction, int files, int merged, int held, int skipped, int unreadable, int milliseconds)
    {
        if (!SyncReceiptParts.Methods.Contains(method))
        {
            return;
        }

        DateTimeOffset now = _time.GetUtcNow();
        lock (_lock)
        {
            CloseQuietRunsBefore(Hour(now));
            if (merged == 0 && held == 0 && unreadable == 0 && skipped == 0 && direction != SyncLogDirection.Send)
            {
                if (!_quiet.TryGetValue((method, direction), out QuietRun? run))
                {
                    run = _quiet[(method, direction)] = new QuietRun(Hour(now));
                }

                run.Passes++;
                run.Files += files;
                run.Milliseconds += milliseconds;
                ScheduleFlush();
                return;
            }

            Add(now, $"{method} {NameOf(direction)} files={files} merged={merged} held={held} skipped={skipped} unreadable={unreadable} ms={milliseconds}");
        }
    }

    /// <summary>A fixed note (a code from <see cref="Notes"/>), never text of any kind.</summary>
    public void Note(string code)
    {
        if (!Notes.Contains(code))
        {
            return;
        }

        lock (_lock)
        {
            Add(_time.GetUtcNow(), "note " + code);
        }
    }

    /// <summary>Writes what is buffered now; <paramref name="everything"/> also closes the current hour's quiet lines (an export).</summary>
    public void Flush(bool everything = false)
    {
        string[] lines;
        lock (_lock)
        {
            CloseQuietRunsBefore(everything ? DateTimeOffset.MaxValue : Hour(_time.GetUtcNow()));
            lines = [.. _buffer];
            _buffer.Clear();
            _flushScheduled = false;
        }

        if (lines.Length == 0)
        {
            return;
        }

        try
        {
            File.AppendAllText(_path, string.Join(Environment.NewLine, lines) + Environment.NewLine, Encoding.UTF8);
            Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log is a help, never a reason to stop.
        }
    }

    /// <summary>Every line kept (the file and what is still buffered), oldest first.</summary>
    public IReadOnlyList<string> Lines()
    {
        Flush(everything: true);
        try
        {
            return File.Exists(_path) ? File.ReadAllLines(_path, Encoding.UTF8) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // A quiet run belongs to its hour: once that hour is over its one line is made.
    private void CloseQuietRunsBefore(DateTimeOffset hour)
    {
        foreach (KeyValuePair<(string Method, SyncLogDirection Direction), QuietRun> pair in _quiet.Where(pair => pair.Value.Hour < hour).ToList())
        {
            _quiet.Remove(pair.Key);
            _buffer.Add($"{Stamp(pair.Value.Hour)} {pair.Key.Method} {NameOf(pair.Key.Direction)} quiet passes={pair.Value.Passes} files={pair.Value.Files} ms={pair.Value.Milliseconds}");
        }
    }

    private void Add(DateTimeOffset at, string text)
    {
        _buffer.Add($"{Stamp(at)} {text}");
        ScheduleFlush();
    }

    private void ScheduleFlush()
    {
        if (!_flushScheduled)
        {
            _flushScheduled = true;
            _ = Task.Delay(FlushAfter).ContinueWith(_ => Flush(), TaskScheduler.Default);
        }
    }

    private void Trim()
    {
        DateTimeOffset now = _time.GetUtcNow();
        if (new FileInfo(_path).Length <= MaxBytes && now - _lastTrim < TrimEvery)
        {
            return;
        }

        _lastTrim = now;
        List<string> lines = File.ReadAllLines(_path, Encoding.UTF8)
            .Where(line => TimeOf(line) is { } at && now - at <= Keep)
            .ToList();
        while (lines.Count > 0 && Encoding.UTF8.GetByteCount(string.Join(Environment.NewLine, lines)) + Environment.NewLine.Length > MaxBytes)
        {
            // Oldest first, a tenth at a time.
            lines.RemoveRange(0, Math.Max(1, lines.Count / 10));
        }

        string temporary = _path + ".tmp";
        File.WriteAllText(temporary, lines.Count == 0 ? string.Empty : string.Join(Environment.NewLine, lines) + Environment.NewLine, Encoding.UTF8);
        File.Move(temporary, _path, overwrite: true);
    }

    private static DateTimeOffset? TimeOf(string line)
    {
        int space = line.IndexOf(' ');
        return space > 0 && DateTimeOffset.TryParse(line.AsSpan(0, space), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at) ? at : null;
    }

    private static string Stamp(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string NameOf(SyncLogDirection direction) => direction switch
    {
        SyncLogDirection.Send => "send",
        SyncLogDirection.Read => "read",
        _ => "history",
    };

    private static DateTimeOffset Hour(DateTimeOffset time) => new(time.UtcDateTime.Year, time.UtcDateTime.Month, time.UtcDateTime.Day, time.UtcDateTime.Hour, 0, 0, TimeSpan.Zero);

    private sealed class QuietRun(DateTimeOffset hour)
    {
        public DateTimeOffset Hour { get; } = hour;

        public int Passes { get; set; }

        public int Files { get; set; }

        public int Milliseconds { get; set; }
    }
}
