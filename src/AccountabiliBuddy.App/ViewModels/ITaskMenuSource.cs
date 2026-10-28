namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A row (a task, a step, an Archive line) that has a ⋯ menu; it is asked for its entries each time the menu opens.</summary>
public interface ITaskMenuSource
{
    IReadOnlyList<TaskMenuEntry> BuildMenu();
}
