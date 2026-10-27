namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Runs the sealed relay program (ADR 46) as a child of the app, one at a time. The child must die with the app, also
/// when the app crashes or is killed. Events can arrive on any thread.
/// </summary>
public interface IRelayProcess : IDisposable
{
    bool IsRunning { get; }

    /// <summary>One line the child wrote to its standard output.</summary>
    event EventHandler<string>? OutputLine;

    /// <summary>One line the child wrote to its error output (a crash message, a port that is taken).</summary>
    event EventHandler<string>? ErrorLine;

    /// <summary>The child ended on its own (crashed or exited), with its exit code. Not raised after <see cref="Stop"/>.</summary>
    event EventHandler<int>? Exited;

    /// <exception cref="InvalidOperationException">A child is already running.</exception>
    /// <exception cref="IOException">The program couldn't be started or tied to the app.</exception>
    void Start(string executablePath, IReadOnlyList<string> arguments);

    /// <summary>Ends the child and everything it started. Does nothing when none runs.</summary>
    void Stop();
}
