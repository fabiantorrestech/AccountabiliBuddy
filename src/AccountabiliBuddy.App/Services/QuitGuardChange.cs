namespace AccountabiliBuddy.App.Services;

/// <summary>What happened to a change of the quit safeguard's settings (ADR 59).</summary>
public enum QuitGuardChange
{
    /// <summary>In force now.</summary>
    Saved,

    /// <summary>Looser: any stricter part is in force now, the rest waits.</summary>
    Waiting,

    /// <summary>Nothing changes during a focus session the user started.</summary>
    RefusedInFocus,

    /// <summary>"All the time" needs a confirmed spare key first.</summary>
    NeedsSpareKeySetup,

    /// <summary>Leaving "All the time" needs the spare key typed.</summary>
    NeedsSpareKey,

    /// <summary>The key typed isn't the spare key.</summary>
    WrongKey,
}
