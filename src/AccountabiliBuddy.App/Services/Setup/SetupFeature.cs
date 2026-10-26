using AccountabiliBuddy.App.ViewModels;

namespace AccountabiliBuddy.App.Services.Setup;

/// <summary>
/// A feature that needs setting up, for the migration assistant ("What's new"): the version it arrived in, whether
/// it applies to this computer, whether it's set up, and the setup assistant page that sets it up.
/// </summary>
public sealed record SetupFeature(
    string Id,
    string Since,
    string Title,
    string Summary,
    Func<bool> IsRelevant,
    Func<bool> IsSetUp,
    AssistantStep Step);
