using System.Text.Json;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// What the PC does with a day plan edit from a paired device (ADR 19), in this order: locked during a focus session
/// the user started; unreadable is invalid; the same plan as the PC's is already applied; an edit made from an older
/// plan is a conflict (the user picks); a plan that breaks the editor's rules is invalid; otherwise it's saved.
/// </summary>
public static class DayPlanEditDecision
{
    /// <param name="currentEditedAt">When the PC's plan last changed, if known (sent with a conflict).</param>
    /// <param name="locked">True while a focus session the user started runs (the Day plan tab is locked too).</param>
    /// <returns>The answer for the device, and the plan to save (null when nothing changes).</returns>
    public static (DayPlanAnswer Answer, DayPlan? ToSave) Decide(DayPlan current, DateTimeOffset? currentEditedAt, DayPlanEdit edit, bool locked)
    {
        if (locked)
        {
            return (new DayPlanAnswer(DayPlanStatus.Locked, "The day plan is locked during the focus session on the PC; it's sent when the session ends."), null);
        }

        DayPlan edited;
        try
        {
            edited = ProfileDocumentSerializer.DeserializeDayPlan(edit.DayPlan);
        }
        catch (JsonException)
        {
            return (new DayPlanAnswer(DayPlanStatus.Invalid, "The PC couldn't read this day plan."), null);
        }

        string currentVersion = DayPlanVersion.Of(current);
        if (DayPlanVersion.Of(edited) == currentVersion)
        {
            return (new DayPlanAnswer(DayPlanStatus.Applied, "The PC already has this plan.", currentVersion), null);
        }

        if (edit.BaseVersion != currentVersion)
        {
            return (new DayPlanAnswer(
                DayPlanStatus.Conflict,
                "The plan was also changed on the PC.",
                currentVersion,
                ProfileDocumentSerializer.SerializeDayPlan(current),
                currentEditedAt), null);
        }

        if (DayPlanValidator.Validate(edited) is string problem)
        {
            return (new DayPlanAnswer(DayPlanStatus.Invalid, problem), null);
        }

        // The new version goes back, so a newer edit waiting on the device can build on this one.
        return (new DayPlanAnswer(DayPlanStatus.Applied, "Saved on the PC.", DayPlanVersion.Of(edited)), edited);
    }
}
