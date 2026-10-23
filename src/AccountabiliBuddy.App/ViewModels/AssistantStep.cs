namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The pages of the setup assistant, in order.</summary>
public enum AssistantStep
{
    Welcome,

    /// <summary>Link to your other devices first (ADR 22): a new device then gets their profiles and plan.</summary>
    LinkFirst,

    /// <summary>What kind of computer this is: Personal, Work or Local only (each a starting set of switches).</summary>
    ComputerType,
    Import,
    Usage,
    Review,
    Browsers,
    Basics,

    /// <summary>The first run's last question: how much focus a day (redesign phase 2).</summary>
    Goal,
    Done,
}
