namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>What one pass over a transport's goal history files did (<see cref="GoalHistorySync.RunAsync"/>): counts only, never a name or a value.</summary>
public sealed class GoalHistoryRun
{
    /// <summary>False when nothing was done: the switch is off, this PC isn't in a group, the goals aren't ready, or the pass wasn't due.</summary>
    public bool Ran { get; set; }

    public int Read { get; set; }

    public int Written { get; set; }

    public int Rolled { get; set; }

    /// <summary>Month files deleted on Drive, or left to expire on the relay, because the year file holds them and every active device merged it.</summary>
    public int Removed { get; set; }

    /// <summary>Own files put again because the relay would expire them (25 days), or because they were missing.</summary>
    public int Reuploaded { get; set; }

    /// <summary>Files not written because the store's size limit would be exceeded.</summary>
    public int TooBig { get; set; }

    /// <summary>Files not written because the relay's file limit (64, with room kept for the devices' sync files) would be exceeded.</summary>
    public int GroupFull { get; set; }

    /// <summary>Listed files that could not be opened (a newer format, or damaged).</summary>
    public int Unreadable { get; set; }

    /// <summary>Files not written (relay only) because a device of the group is on an app that doesn't know history files.</summary>
    public int Gated { get; set; }

    /// <summary>The pass stopped at the store's request cap; the rest is done at the next pass.</summary>
    public bool Capped { get; set; }

    public override string ToString()
    {
        return $"read={Read} written={Written} rolled={Rolled} removed={Removed} reuploaded={Reuploaded} tooBig={TooBig} groupFull={GroupFull} unreadable={Unreadable} gated={Gated} capped={(Capped ? 1 : 0)}";
    }
}
