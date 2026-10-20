namespace AccountabiliBuddy.App.Services;

/// <summary>Where the user is in the Google setup guide (documents "googleSetup"), so it can be picked up later.</summary>
/// <param name="DoneStepIds">Steps marked done.</param>
/// <param name="IncludeSleep">Also set up sleep tracking (Google Health).</param>
public sealed record GoogleSetupProgress(IReadOnlyList<string> DoneStepIds, bool IncludeSleep)
{
    public static GoogleSetupProgress Empty { get; } = new(Array.Empty<string>(), false);
}
