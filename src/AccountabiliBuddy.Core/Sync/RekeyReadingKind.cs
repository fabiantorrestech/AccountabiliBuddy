namespace AccountabiliBuddy.Core.Sync;

/// <summary>What a rekey file means for the device that reads it (ADR 54).</summary>
public enum RekeyReadingKind
{
    /// <summary>The device is among the removed ones.</summary>
    Removed,

    /// <summary>The device stays and unwrapped the new link code.</summary>
    NewKey,

    /// <summary>The device isn't removed but has no wrap it can open (it had no public key when the rekey was made): it is cut off all the same.</summary>
    CutOff,
}
