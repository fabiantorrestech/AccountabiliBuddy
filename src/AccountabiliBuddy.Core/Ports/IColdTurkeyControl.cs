namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Switches and locks Cold Turkey blocks through its command line (ADR 37, 39); its settings file is never written.
/// Each call returns true when Cold Turkey accepted it (the caller confirms the state by reading the settings again).
/// </summary>
public interface IColdTurkeyControl
{
    /// <summary>True when Cold Turkey is installed and its command line can be run.</summary>
    bool IsAvailable { get; }

    /// <summary>Starts <paramref name="block"/>, locked for <paramref name="lockMinutes"/> or with <paramref name="password"/> when given.</summary>
    Task<bool> StartAsync(string block, int? lockMinutes = null, string? password = null);

    /// <summary>Stops <paramref name="block"/>; a password-locked block needs its <paramref name="password"/>.</summary>
    Task<bool> StopAsync(string block, string? password = null);
}
