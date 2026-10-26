namespace AccountabiliBuddy.App.ViewModels;

/// <summary>What the Cold Turkey key window opens for (ADR 39, 41).</summary>
public enum ColdTurkeyKeyMode
{
    /// <summary>"Your spare key": a new key shown once and typed back.</summary>
    SpareKey,

    /// <summary>"Emergency release": the spare key typed releases every held block for today.</summary>
    Emergency,

    /// <summary>"Get a new spare key": a reason, a retyped code and a 10-minute wait, then a new key (ADR 41).</summary>
    NewKey,
}
