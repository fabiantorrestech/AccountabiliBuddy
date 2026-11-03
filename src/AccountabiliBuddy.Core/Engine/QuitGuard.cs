using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// The quit safeguard's rules (ADR 59): whether AB may be quit now, and what counts as a looser change. Pure, so it can be checked
/// on its own; the app supplies what is going on now.
/// </summary>
public static class QuitGuard
{
    // How many ticked parts in a row are followed to find when the lock ends.
    private const int MaxPartsFollowed = 24;

    /// <summary>The categories it locks during: the chosen ones, else every work category and Pre-Work.</summary>
    public static IReadOnlyCollection<Guid> CategoriesOf(QuitGuardSettings settings, DayPlan plan)
    {
        return settings.Categories
            ?? plan.Categories
                .Where(category => category.Behavior.IsWork() || category.Behavior == CategoryBehavior.PreWork)
                .Select(category => category.Id)
                .ToList();
    }

    /// <summary>What holds now, or null when AB may be quit.</summary>
    /// <param name="partAt">The day plan's part at a local time; null when none.</param>
    public static QuitLock? Check(QuitGuardSettings settings, bool inFocusSession, DayPlan plan, DateTime now, Func<DateTime, PlannedPart?> partAt)
    {
        if (!settings.On)
        {
            return null;
        }

        if (settings.AllTheTime)
        {
            return new QuitLock(QuitLockReason.AllTheTime);
        }

        if (settings.DuringSession && inFocusSession)
        {
            return new QuitLock(QuitLockReason.Session);
        }

        IReadOnlyCollection<Guid> categories = CategoriesOf(settings, plan);
        if (partAt(now) is not PlannedPart part || !categories.Contains(part.Category.Id))
        {
            return null;
        }

        DateTime until = part.Window.End;
        for (int followed = 0; followed < MaxPartsFollowed; followed++)
        {
            if (partAt(until) is not PlannedPart next || !categories.Contains(next.Category.Id) || next.Window.End <= until)
            {
                break;
            }

            until = next.Window.End;
        }

        return new QuitLock(QuitLockReason.Category, part.Category.Name, until);
    }

    /// <summary>True when going from one to the other drops any protection: such a change waits.</summary>
    public static bool Loosens(QuitGuardSettings before, QuitGuardSettings to, DayPlan plan)
    {
        if (!before.On)
        {
            return false;
        }

        return !to.On
            || (before.AllTheTime && !to.AllTheTime)
            || (before.DuringSession && !to.DuringSession)
            || CategoriesOf(before, plan).Except(CategoriesOf(to, plan)).Any();
    }

    /// <summary>True when the change leaves hardcore: it needs the spare key as well as the wait.</summary>
    public static bool LeavesHardcore(QuitGuardSettings before, QuitGuardSettings to)
    {
        return before.On && before.AllTheTime && !(to.On && to.AllTheTime);
    }

    /// <summary>What applies while a looser change waits: everything in force now, plus anything the change adds.</summary>
    public static QuitGuardSettings StricterOf(QuitGuardSettings before, QuitGuardSettings to, DayPlan plan)
    {
        if (!to.On)
        {
            return before;
        }

        return before with
        {
            AllTheTime = before.AllTheTime || to.AllTheTime,
            DuringSession = before.DuringSession || to.DuringSession,
            Categories = CategoriesOf(before, plan).Union(CategoriesOf(to, plan)).ToList(),
        };
    }
}
