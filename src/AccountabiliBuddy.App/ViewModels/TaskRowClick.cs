namespace AccountabiliBuddy.App.ViewModels;

/// <summary>How a task row was clicked (Windows selection rules, spec 05 §12).</summary>
public enum TaskRowClick
{
    /// <summary>A plain click (or Enter): opens or closes the task and clears the selection.</summary>
    Open,

    /// <summary>Ctrl+click: adds the row to the selection or takes it out.</summary>
    Toggle,

    /// <summary>Shift+click: selects the rows from the last clicked one to this one (same panel, visible order).</summary>
    Range,

    /// <summary>Ctrl+Shift+click: adds that range to the selection.</summary>
    AddRange,
}
