using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Ports;

/// <summary>Reads why the PC last went down, from what Windows recorded (ADR 60).</summary>
public interface IShutdownCauseReader
{
    /// <summary>The newest cause recorded after <paramref name="since"/>; <see cref="ShutdownCause.Unknown"/> when none can be read.</summary>
    ShutdownCause Read(DateTimeOffset since);
}
