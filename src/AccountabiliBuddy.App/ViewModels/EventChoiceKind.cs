namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What a row of the calendar event picker does (ADR 58).</summary>
public enum EventChoiceKind
{
    /// <summary>Gives the event (or its series) a day plan category.</summary>
    Category,

    /// <summary>Stops a calendar rule from giving the event a category.</summary>
    DontClassify,

    /// <summary>Takes the choice back, so the rules or the plan decide again.</summary>
    Automatic,
}
