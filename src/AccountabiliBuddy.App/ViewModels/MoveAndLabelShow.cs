namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What Move and label's task list shows (its Show dropdown, checklist 8c.09-8c.11, 8c.29).</summary>
public enum MoveAndLabelShow
{
    /// <summary>"All (5)": every task brought along.</summary>
    All,

    /// <summary>"Not done yet (4)": the rows not ticked.</summary>
    NotDone,

    /// <summary>"Done (1)": the ticked rows.</summary>
    Done,
}
