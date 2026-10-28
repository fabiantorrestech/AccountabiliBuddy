namespace AccountabiliBuddy.App.Services;

/// <summary>When the title bar's sync light shows (document "syncLight").</summary>
public enum SyncLightMode
{
    /// <summary>Whenever another device is linked (the default).</summary>
    Always,

    /// <summary>Only while something is wrong: a device is behind, or this PC can't sync.</summary>
    OnlyWhenWrong,

    /// <summary>Never; the sync button next to it stays.</summary>
    Off,
}
