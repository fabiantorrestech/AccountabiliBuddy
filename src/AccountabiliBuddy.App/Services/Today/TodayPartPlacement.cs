namespace AccountabiliBuddy.App.Services.Today;

/// <summary>Where a part of the Today sidebar sits (Settings › Today, round 2 brief §4.2): a panel, a chip under the panels, or off.</summary>
public enum TodayPartPlacement
{
    /// <summary>In the sidebar as a panel, in the chosen order.</summary>
    Panel,

    /// <summary>A small button in the chip row under the panels; a click opens the same panel next to it.</summary>
    Chip,

    /// <summary>Not drawn.</summary>
    Off,
}
