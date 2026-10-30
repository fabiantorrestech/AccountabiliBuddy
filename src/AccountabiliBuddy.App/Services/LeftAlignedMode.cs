namespace AccountabiliBuddy.App.Services;

/// <summary>Where the overlay goes on a taskbar whose app icons start at the left (Windows 10, or Windows 11 set to left); stored as a number.</summary>
public enum LeftAlignedMode
{
    /// <summary>Against the tray.</summary>
    BesideTray,

    /// <summary>Right after the last app icon, moving as apps open and close.</summary>
    FollowApps,
}
