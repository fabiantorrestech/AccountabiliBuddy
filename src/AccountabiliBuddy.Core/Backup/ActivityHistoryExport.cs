namespace AccountabiliBuddy.Core.Backup;

/// <summary>
/// The activity history in a backup: per table (activity_segments, activity_labels, session_events) its rows as
/// column → text. Columns ending in "_protected" hold the decrypted text in a backup with secrets and are left out
/// otherwise; the row id is never included. Rows as text keep old backups readable after schema changes.
/// </summary>
public sealed record ActivityHistoryExport(IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string?>>> Tables)
{
    public int RowCount => Tables.Values.Sum(rows => rows.Count);
}
