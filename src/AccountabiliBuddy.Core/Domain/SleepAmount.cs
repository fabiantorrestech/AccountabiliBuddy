namespace AccountabiliBuddy.Core.Domain;

/// <summary>How a night's sleep length rates.</summary>
public enum SleepAmount
{
    /// <summary>7 hours or more.</summary>
    Good,

    /// <summary>5 hours or more, under 7.</summary>
    Okay,

    /// <summary>Under 5 hours.</summary>
    Short,
}
