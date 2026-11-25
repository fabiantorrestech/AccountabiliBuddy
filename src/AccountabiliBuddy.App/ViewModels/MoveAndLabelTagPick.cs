namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A tag picked in Move and label's Tags: on for every task being edited, or off all of them (checklist 8c.53-8c.55).</summary>
public enum MoveAndLabelTagPick
{
    /// <summary>"+#quick": the tag goes on every task being edited.</summary>
    Add,

    /// <summary>"−#waiting": the tag comes off every task being edited (offered when they all have it).</summary>
    Remove,
}
