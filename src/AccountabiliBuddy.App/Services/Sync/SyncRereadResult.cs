namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>What one "Re-read everything" over a method did (SYNC1b): counts only.</summary>
/// <param name="Method">The method (<see cref="Core.Sync.SyncReceiptParts.Methods"/>).</param>
/// <param name="Files">The files read and merged again.</param>
/// <param name="Problems">The files that could not be read (damaged, too big, newer app) or not downloaded.</param>
/// <param name="Requests">The requests made to the method (the relay's pacing and quota are checked on this).</param>
/// <param name="Stopped">The pass stopped early: the relay answered 429 (too many requests), or the method went away.</param>
public sealed record SyncRereadResult(string Method, int Files, int Problems, int Requests, bool Stopped)
{
    /// <summary>Nothing done (the method is off or the PC is in no group).</summary>
    public static SyncRereadResult None(string method) => new(method, 0, 0, 0, false);
}
