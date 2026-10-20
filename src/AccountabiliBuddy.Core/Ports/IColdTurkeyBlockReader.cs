using AccountabiliBuddy.Core.Import;

namespace AccountabiliBuddy.Core.Ports;

/// <summary>Which Cold Turkey blocks are switched on, from Cold Turkey's own settings (read only, never changed).</summary>
public interface IColdTurkeyBlockReader
{
    /// <returns>The blocks, or null when Cold Turkey isn't installed or its settings can't be read.</returns>
    ColdTurkeyBlocks? Read();
}
