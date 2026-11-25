namespace AccountabiliBuddy.App.ViewModels;

/// <summary>How a task row was clicked (Windows selection rules, spec 05 §12).</summary>
public enum TaskRowClick
{
    /// <summary>A plain click (or Enter): opens or closes the task and clears the selection.</summary>
    Open,

    /// <summary>Ctrl+click: adds the row to the selection or takes it out.</summary>
    Toggle,

    /// <summary>Shift+↑/↓: selects the rows from the last picked one to this one (same panel, visible order), in place of the selection.</summary>
    Range,

    /// <summary>Shift+click (and Ctrl+Shift+click): adds the rows from the last picked one to this one to the selection (Tasks redesign 8a.03).</summary>
    AddRange,
}
