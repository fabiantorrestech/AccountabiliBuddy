using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Link;

namespace AccountabiliBuddy.App.Services.Remote;

/// <summary>
/// A paired phone's day plan edit (ADR 19), however it arrived: over the link or in a sync file (ADR 42). Decided by
/// <see cref="DayPlanEditDecision"/> (locked during a focus session, a clash answered with the PC's version and edit
/// time, checked like the Day plan tab) and saved through <see cref="ConfigurationService"/>, so a cut of today's work
/// time on a guarded computer waits like a looser change (ADR 28). UI thread only: the callers move there.
/// </summary>
public sealed class RemotePlanEdits
{
    private readonly SessionController _sessions;
    private readonly ConfigurationService _configuration;
    private readonly DayPlanEditTimeService _planEditTime;

    public RemotePlanEdits(SessionController sessions, ConfigurationService configuration, DayPlanEditTimeService planEditTime)
    {
        _sessions = sessions;
        _configuration = configuration;
        _planEditTime = planEditTime;
    }

    /// <summary>When the PC's plan last changed, if known (sent to devices with its version).</summary>
    public DateTimeOffset? PlanEditedAt => _planEditTime.Current;

    public async Task<LinkDayPlanAnswer> ApplyAsync(LinkDayPlanEdit edit)
    {
        if (_configuration.IsReadOnly)
        {
            // It couldn't be saved; the device keeps its edit instead of being told "Saved".
            return new LinkDayPlanAnswer(LinkDayPlanStatus.Invalid, "The PC's settings are read-only right now (see the banner in its window).");
        }

        (LinkDayPlanAnswer answer, DayPlan? toSave) = DayPlanEditDecision.Decide(
            _configuration.Current.DayPlan, _planEditTime.Current, edit, locked: _sessions.IsEnforcing);
        if (toSave is not null)
        {
            _planEditTime.ExpectEdit(edit.EditedAt);
            await _configuration.UpdateAsync(document => document with { DayPlan = toSave });
            if (_configuration.DayPlanWaitText is string waits)
            {
                return answer with { Message = $"On the PC: {waits}" };
            }
        }

        return answer;
    }
}
