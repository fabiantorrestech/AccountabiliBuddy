namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Where an add box sits (spec 03 §B, B2): it decides what the box shows around the same typing rules.</summary>
public enum AddBoxKind
{
    /// <summary>The Tasks page's "+ Add a task" pill: Send to, title help, project markers.</summary>
    Main,

    /// <summary>A Shelf bin's "+": the same box inside the section, its Send to preset to that bin; Esc or leaving it empty closes it.</summary>
    Bin,

    /// <summary>Jot it down (Today page): a faint pill; Enter adds to Shelf · To sort, Esc closes; @label and #tag only.</summary>
    Jot,
}
