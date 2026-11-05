using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// What Start shutdown would end early now (ADR 65): the rest of the current work block, and the work blocks that start later
/// the same plan date (ended too only when the user ticks "Also end later work today"). Local times.
/// </summary>
public sealed record WorkEndProposal(DateOnly PlanDate, LocalWindow Current, IReadOnlyList<LocalWindow> Later)
{
    public bool HasLater => Later.Count > 0;
}
