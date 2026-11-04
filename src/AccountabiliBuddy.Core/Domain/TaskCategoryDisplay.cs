namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// The name and colour to show for something in a task category. A task, Later item or come-back keeps the name and colour it had when
/// they were stored, so a category that was renamed or recoloured since would show its old look. The category as it is now wins.
/// </summary>
public static class TaskCategoryDisplay
{
    /// <summary>The category with <paramref name="id"/>, else the one named <paramref name="name"/> (any case), else the stored values (a deleted category keeps its look).</summary>
    /// <param name="id">The category's id on this device; null when the item carries only a name.</param>
    /// <returns>Both null for something with no category.</returns>
    public static (string? Name, string? Color) Resolve(IReadOnlyList<TaskCategory> categories, Guid? id, string? name, string? storedColor)
    {
        TaskCategory? current = categories.FirstOrDefault(category => category.Id == id)
            ?? (name is null ? null : categories.FirstOrDefault(category => string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase)));
        return current is null ? (name, storedColor) : (current.Name, current.Color);
    }
}
