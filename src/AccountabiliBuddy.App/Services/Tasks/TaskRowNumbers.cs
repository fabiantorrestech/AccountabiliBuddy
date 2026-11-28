using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// "Number the tasks" (owner 2026-11-27; Settings › Tasks, Tasks › Options; on by default): whether task rows show their place in their list
/// (1, 2, 3 …) left of the check box, in the Shelf's columns, Today's lists and the Archive sheet. This PC only, both modes
/// (<see cref="TaskLayoutPreferences.NumberRows"/>). App-wide by nature, so one shared instance the row templates bind to
/// (<see cref="Shared"/>); This session's own numbers show either way. UI thread only.
/// </summary>
public sealed partial class TaskRowNumbers : ObservableObject
{
    private TaskRowNumbers()
    {
        IsOn = true;
    }

    /// <summary>The instance every row template reads.</summary>
    public static TaskRowNumbers Shared { get; } = new();

    /// <summary>Rows show their place in their list.</summary>
    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>Applies the store's value now and after every change.</summary>
    public static void Follow(TaskLayoutStore layout)
    {
        Shared.IsOn = layout.Current.NumberRows;
        layout.Changed += (_, _) => Shared.IsOn = layout.Current.NumberRows;
    }
}
