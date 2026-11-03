namespace AccountabiliBuddy.Core.Domain;

/// <summary>What a calendar event classification covers (ADR 58).</summary>
public enum ClassificationScope
{
    /// <summary>One event, or one occurrence of a repeating event.</summary>
    ThisEvent,

    /// <summary>Every occurrence of a repeating event.</summary>
    WholeSeries,
}
