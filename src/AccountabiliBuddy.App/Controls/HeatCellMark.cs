namespace AccountabiliBuddy.App.Controls;

/// <summary>
/// How a heatmap cell is drawn beyond its ramp level (brief §3.8, the goal heatmap's hit / missed / today states).
/// </summary>
public enum HeatCellMark
{
    /// <summary>Filled with its ramp level (hit days; every Stats cell).</summary>
    None,

    /// <summary>A missed day: a hollow outline, no fill.</summary>
    Hollow,

    /// <summary>Some time, under the target: a faint fill inside the outline.</summary>
    Faint,

    /// <summary>Today: filled from the bottom with its progress (the accent outline is the control's today ring).</summary>
    Today,

    /// <summary>A paused day: neither hit nor missed, drawn hatched.</summary>
    Paused,

    /// <summary>A day still to come this week: a faint dashed outline.</summary>
    Future,
}
