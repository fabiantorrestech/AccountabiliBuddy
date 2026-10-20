namespace AccountabiliBuddy.App.Services;

/// <summary>One step of the Google setup guide.</summary>
/// <param name="Id">Stable id, used to remember which steps are done.</param>
/// <param name="Instructions">What to click, one action per line.</param>
/// <param name="PageUrl">The Google Cloud Console page for this step; null when the step happens in the app.</param>
/// <param name="CopyValues">Exact values to paste, with a label each.</param>
/// <param name="ForSleepOnly">Only needed for sleep tracking (Google Health).</param>
public sealed record GoogleSetupStep(
    string Id,
    string Title,
    IReadOnlyList<string> Instructions,
    string? PageUrl,
    IReadOnlyList<GoogleSetupCopyValue> CopyValues,
    bool ForSleepOnly = false);
