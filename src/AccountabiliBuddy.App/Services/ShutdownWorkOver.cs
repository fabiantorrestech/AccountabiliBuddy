namespace AccountabiliBuddy.App.Services;

/// <summary>
/// "Work over for today": the routine was finished and the next work part has not started. Today says so, the work-start nudges stay
/// quiet and the capture box says a thought goes to tomorrow. Nothing is locked.
/// </summary>
/// <param name="Line">The closing line.</param>
/// <param name="Checked">What was checked.</param>
/// <param name="Until">When it ends: the next work part, or 4:00 AM the next morning when that comes first.</param>
public sealed record ShutdownWorkOver(string Line, IReadOnlyList<string> Checked, DateTime Until)
{
    /// <summary>What was checked, as one line.</summary>
    public string CheckedText => Checked.Count == 0 ? string.Empty : string.Join(" · ", Checked);
}
