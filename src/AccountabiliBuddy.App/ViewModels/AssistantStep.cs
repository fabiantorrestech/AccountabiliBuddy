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

    /// <summary>ADR 47 (C 15): which days are workdays, and a work window or a budget of hours for them.</summary>
    Workdays,

    /// <summary>ADR 39: which Cold Turkey blocks the app holds, where, and Cold Turkey's settings for it.</summary>
    ColdTurkeyKeys,

    /// <summary>ADR 39: the spare key, shown once and typed back.</summary>
    SpareKey,

    /// <summary>ADR 46: the own relay, a pointer to Devices → Own relay (only in the migration assistant).</summary>
    OwnRelay,

    /// <summary>The Setup DP Assistant, a button on the Day plan tab: a pointer page with an "Open it now" button (only in the migration assistant).</summary>
    DpAssistant,

    /// <summary>The start-of-day assistant on the first arrival of a workday: on or off (only in the migration assistant).</summary>
    StartOfDay,

    /// <summary>The first run's last question: how much focus a day (redesign phase 2).</summary>
    Goal,
    Done,
}
