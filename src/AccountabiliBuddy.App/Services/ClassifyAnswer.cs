using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>The user's answer to the classify prompt.</summary>
/// <param name="Rule">The rule the answer stands for.</param>
/// <param name="Snapshot">The activity it was about.</param>
/// <param name="Lifetime">Session: allowed until the session ends, nothing saved. Permanent: saved as a rule.</param>
/// <param name="Reason">Why, as typed (required to allow anything); null when none was given.</param>
/// <param name="OtherProfileIds">Further profiles a permanent exemption is added to, besides the session's.</param>
public sealed record ClassifyAnswer(
    Rule Rule,
    ActivitySnapshot Snapshot,
    LabelLifetime Lifetime,
    string? Reason,
    IReadOnlyList<Guid> OtherProfileIds);
