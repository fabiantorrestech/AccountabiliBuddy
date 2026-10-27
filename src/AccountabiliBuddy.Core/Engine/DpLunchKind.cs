namespace AccountabiliBuddy.Core.Engine;

/// <summary>How the Setup DP Assistant sets up lunch.</summary>
public enum DpLunchKind
{
    /// <summary>A lunch window the user starts lunch in ("Start lunch", schema v8).</summary>
    Flexible,

    /// <summary>A fixed lunch period that starts by itself (schema v13).</summary>
    Fixed,

    /// <summary>The day's lunch stays as it is (a plain Rest "Lunch" part, neither a window nor starting by itself).</summary>
    Keep,
}
