namespace AccountabiliBuddy.App.Services.Setup;

/// <summary>
/// Every feature a user may need to set up after an upgrade (project rule: a new or reworked feature that needs
/// setting up gets an entry here with the version it arrives in, its "set up?" check and its assistant page). The
/// migration assistant (5 taps on the About logo; a quiet Today card after an upgrade) runs the pages of the ones not
/// set up yet.
/// </summary>
public static class SetupFeatures
{
    public static IReadOnlyList<SetupFeature> All(ColdTurkeyKeysService keys, ColdTurkeyService coldTurkey) => new[]
    {
        new SetupFeature(
            "coldturkey.keys", "0.1.106",
            "Cold Turkey: let AccountabiliBuddy hold the keys",
            "Locks the Cold Turkey blocks you pick during focus sessions, Pre-Work and work hours, with a spare key for emergencies.",
            IsRelevant: () => coldTurkey.Blocks is not null,
            IsSetUp: () => keys.Held.Count > 0 && keys.HasSpareKey,
            Step: ViewModels.AssistantStep.ColdTurkeyKeys),
    };
}
