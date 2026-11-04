namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The shutdown routine's settings (inside the device-local "shutdown" document; Settings › Your day). Turning anything off here is
/// never a looser guard: the routine enforces nothing.
/// </summary>
/// <param name="Enabled">Off: no card at the end of work and no reminder; "Start shutdown" stays available.</param>
/// <param name="ClosingLine">What the closing screen and Today say; blank = the default.</param>
/// <param name="ReminderMinutes">How long before work ends the reminder comes; 0 = no reminder.</param>
public sealed record ShutdownSettings(
    bool Enabled = true,
    bool Capture = true,
    bool Unfinished = true,
    bool LookAhead = true,
    bool PlanTomorrow = true,
    bool Review = true,
    string ClosingLine = ShutdownSettings.DefaultClosingLine,
    int ReminderMinutes = ShutdownSettings.DefaultReminderMinutes)
{
    public const string DefaultClosingLine = "Work over for today.";
    public const int DefaultReminderMinutes = 15;
    public const int MaxReminderMinutes = 120;
    public const int MaxClosingLineLength = 80;

    public static ShutdownSettings Default { get; } = new();

    /// <summary>The steps these settings show, in order, ending with the closing screen.</summary>
    public IReadOnlyList<ShutdownStep> Steps
    {
        get
        {
            var steps = new List<ShutdownStep>();
            AddIf(steps, Capture, ShutdownStep.Capture);
            AddIf(steps, Unfinished, ShutdownStep.Unfinished);
            AddIf(steps, LookAhead, ShutdownStep.LookAhead);
            AddIf(steps, PlanTomorrow, ShutdownStep.PlanTomorrow);
            AddIf(steps, Review, ShutdownStep.Review);
            steps.Add(ShutdownStep.Closing);
            return steps;
        }
    }

    /// <summary>The settings held to their limits: a blank line becomes the default, the reminder 0 to 120 minutes.</summary>
    public ShutdownSettings Normalized()
    {
        string line = ClosingLine?.Trim() ?? string.Empty;
        if (line.Length == 0)
        {
            line = DefaultClosingLine;
        }
        else if (line.Length > MaxClosingLineLength)
        {
            line = line[..MaxClosingLineLength].TrimEnd();
        }

        return this with { ClosingLine = line, ReminderMinutes = Math.Clamp(ReminderMinutes, 0, MaxReminderMinutes) };
    }

    private static void AddIf(List<ShutdownStep> steps, bool on, ShutdownStep step)
    {
        if (on)
        {
            steps.Add(step);
        }
    }
}
