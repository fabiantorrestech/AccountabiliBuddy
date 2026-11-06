using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// An armed "ProductaBuddy now" during a focus session (ADR 68, 66): once what ending the session early costs (its wait) is over,
/// the switch's own looser-change wait starts, and it applies even if the session still runs. The reason is kept sealed with the
/// mode. Dropped when there is nothing left to switch (ProductaBuddy took effect some other way) or a mode was chosen after it was
/// armed (back to AccountabiliBuddy, Cancel, or a newer choice on another device).
/// </summary>
public sealed class CompanionArmedEndHandler : IArmedEndHandler
{
    private readonly GuidanceModeService _guidance;

    public CompanionArmedEndHandler(GuidanceModeService guidance)
    {
        _guidance = guidance;
    }

    public ArmedEndKind Kind => ArmedEndKind.Companion;

    public string Label(ArmedEnd end) => $"Switch to {AppBrand.CompanionName}";

    public string DoneTitle(ArmedEnd end) => $"Switching to {AppBrand.CompanionName}";

    public bool IsStillDue(ArmedEnd end) =>
        !_guidance.IsCompanion && (_guidance.State.EditedAt is not DateTimeOffset chosen || chosen <= end.RequestedAt);

    public async Task<string> ApplyAsync(ArmedEnd end, string reason)
    {
        await _guidance.RequestCompanionNowAsync(reason);
        return $"{AppBrand.CompanionName} takes effect after its wait, during this session too. Cancel it from Today or the on-screen tile.";
    }
}
