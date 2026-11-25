using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// GS5: the task menu's "Link to goal" entry. It lists the goals a task can carry (the task's own ticked, its name at the right), ends with
/// "Unlink" and is greyed with a reason while there is no goal to link to. A link to a goal that no longer exists is not in the list, so it
/// shows as none (the id stays on the task, so a restored goal gets its tasks back).
/// </summary>
public static class TaskGoalMenu
{
    /// <param name="goals">The goals that can be linked to (id, name, icon), in the Goals page's order.</param>
    /// <param name="current">The goal of a single task (null for none, and for a selection of several).</param>
    /// <param name="setGoal">Links the task or tasks to the goal (id and name), or to none (null, null).</param>
    /// <param name="count">
    /// How many tasks it acts on: "Link to goal (3)" for 2 or more, plain "Link to goal" for one (the counts rule, Tasks redesign 8a.41).
    /// </param>
    public static TaskMenuEntry Entry(
        IReadOnlyList<(Guid Id, string Name, MaterialIconKind Icon)> goals, Guid? current, Func<Guid?, string?, Task> setGoal, int count = 1)
    {
        string header = SelectionCountText.Action("Link to goal", count);
        if (goals.Count == 0)
        {
            return new TaskMenuEntry(header, Icon: MaterialIconKind.LinkVariant, Note: "No goals yet", Id: "goal");
        }

        var children = new List<TaskMenuEntry>();
        foreach ((Guid id, string name, MaterialIconKind icon) in goals)
        {
            Guid goalId = id;
            string goalName = name;
            children.Add(new TaskMenuEntry(name, new AsyncRelayCommand(() => setGoal(goalId, goalName)), IsChecked: current == id, Icon: icon));
        }

        children.Add(TaskMenuEntry.Separator);
        children.Add(new TaskMenuEntry("Unlink", new AsyncRelayCommand(() => setGoal(null, null)), Icon: MaterialIconKind.LinkVariantOff));
        string? linked = goals.Where(goal => goal.Id == current).Select(goal => goal.Name).FirstOrDefault();
        return new TaskMenuEntry(header, Children: children, Icon: MaterialIconKind.LinkVariant, Value: linked, Id: "goal");
    }
}
