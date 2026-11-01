namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Where the removed device's backup goes (ADR 55).</summary>
public enum RemoveBackupChoice
{
    /// <summary>The app's Drive folder, when Drive is a sync method in use.</summary>
    Drive,

    /// <summary>A file on this PC.</summary>
    File,

    /// <summary>No backup.</summary>
    None,
}
