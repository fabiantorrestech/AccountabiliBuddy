using System.Globalization;
using System.Text;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The diagnostics report (SYNC1b): the confirmation board, the unreadable files, the versions and the sync log as one text file for the user
/// to keep or send by hand. It is never sent anywhere automatically. Like the log it can't hold private text: devices appear as "PC 1" and
/// "Phone 1" with their kind and app version, files by their reason and method only (never a name, path, address or id), and nothing from
/// the user's own data. A pure builder; the Save as dialog and the folder check (<see cref="RefusalFor"/>) are the caller's.
/// </summary>
public static class SyncDiagnosticsExport
{
    /// <summary>The report's text.</summary>
    /// <param name="selfVersion">This PC's app version.</param>
    /// <param name="board">The confirmation board.</param>
    /// <param name="unreadable">The unreadable and leftover files (all, including ignored ones, marked as such).</param>
    /// <param name="ordinals">Each device's ordinal (<see cref="SyncBoardBuilder.Ordinals"/>).</param>
    /// <param name="logLines">The sync log's lines.</param>
    public static string Build(
        DateTimeOffset now, TimeZoneInfo zone, string selfVersion, SyncBoard board, IReadOnlyList<SyncUnreadableFile> unreadable,
        IReadOnlyDictionary<Guid, string> ordinals, IReadOnlyList<string> logLines)
    {
        var text = new StringBuilder();
        text.AppendLine("AccountabiliBuddy sync diagnostics");
        text.AppendLine("Made " + SyncBoardWords.Clock(now, now, zone) + " (this PC's local time, " + now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ")");
        text.AppendLine("This PC: app " + selfVersion);
        text.AppendLine("No names, text, addresses or file names are in this report: devices are numbered by kind.");
        text.AppendLine();
        text.AppendLine("== Confirmation board ==");
        if (board.NotSyncing)
        {
            text.AppendLine("Not syncing: no sync method is on.");
        }

        foreach (SyncBoardMethod method in board.Methods)
        {
            text.AppendLine(method.Title);
            if (method.Devices.Count == 0)
            {
                text.AppendLine("  No other devices yet.");
            }

            foreach (SyncBoardDevice device in method.Devices)
            {
                text.AppendLine($"  {device.Ordinal} ({device.Kind}, app {device.AppVersion}): {device.Dot.Color} - {SyncBoardWords.Words(device.Dot)}"
                    + (device.ConfirmedFile is { } confirmed ? $"; confirmed this PC's file from {SyncBoardWords.Clock(confirmed, now, zone)}" : string.Empty)
                    + (device.LastFileAt is { } last ? $"; its newest file {SyncBoardWords.Clock(last, now, zone)}" : string.Empty));
                foreach (SyncBoardPart part in device.Parts)
                {
                    text.AppendLine($"      {SyncBoardWords.PartName(part.Code)}: {part.Dot.Color} - {SyncBoardWords.Words(part.Dot)}");
                }
            }
        }

        text.AppendLine();
        text.AppendLine("== Files that aren't taken in ==");
        if (unreadable.Count == 0)
        {
            text.AppendLine("None.");
        }

        foreach (SyncUnreadableFile file in unreadable)
        {
            string who = file.Device is Guid id && ordinals.TryGetValue(id, out string? ordinal) ? ordinal : file.OwnStale ? "this PC" : "a device";
            text.AppendLine($"{SyncBoardBuilder.TitleOf(file.Method)}; {who}; {SyncBoardWords.Clock(file.At, now, zone)}; {SyncBoardWords.ReasonOfFile(file.Reason)}" + (file.Ignored ? "; ignored" : string.Empty));
        }

        text.AppendLine();
        text.AppendLine("== Sync log (last 7 days, UTC) ==");
        if (logLines.Count == 0)
        {
            text.AppendLine("Empty.");
        }

        foreach (string line in logLines)
        {
            text.AppendLine(line);
        }

        return text.ToString();
    }

    /// <summary>
    /// Why <paramref name="path"/> may not be used for the report, or null when it may: a place inside the sync folder would be carried off by
    /// whatever syncs that folder (Syncthing, Drive for desktop), which is the one thing the report must never do.
    /// </summary>
    /// <param name="syncFolder">The configured sync folder; null or empty when none is set.</param>
    public static string? RefusalFor(string path, string? syncFolder)
    {
        if (string.IsNullOrWhiteSpace(syncFolder))
        {
            return null;
        }

        try
        {
            string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(syncFolder)) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(path);
            return target.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
                ? "Save the report outside your sync folder: whatever syncs that folder would carry it to other places."
                : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "That isn't a place the report can be saved.";
        }
    }
}
