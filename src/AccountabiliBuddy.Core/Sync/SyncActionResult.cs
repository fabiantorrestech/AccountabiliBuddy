using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// A PC's answer to one <see cref="SyncAction"/> (ADR 42), in its own file's <see cref="SyncFile.ActionResults"/>. A PC
/// that writes results writes one for every action it saw, kinds it doesn't know included (not supported), so a phone
/// tells a refusal from an old PC's silent ack. Kept while the action is still in the phone's file (14 days at most).
/// </summary>
/// <param name="Id">The action's id.</param>
/// <param name="Ok">Applied (or nothing left to do); false when refused.</param>
/// <param name="Status">One of the <see cref="SyncActionStatus"/> values.</param>
/// <param name="Message">For the phone to show, e.g. why it was refused; null when there's nothing to say.</param>
/// <param name="PlanVersion">A day plan edit's: the PC's plan version now (on a clash, and after saving).</param>
/// <param name="PlanEditedAt">A day plan clash's: when the PC's plan last changed, if known.</param>
public sealed record SyncActionResult(
    Guid Id,
    bool Ok,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PlanVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? PlanEditedAt = null)
{
    /// <summary>The message for a kind this PC doesn't know.</summary>
    public const string NotSupportedMessage = "Not supported by this version";

    /// <summary>The message for an action that arrived after its freshness ran out (never applied late).</summary>
    public const string TooLateMessage = "Too late — try again";
}
