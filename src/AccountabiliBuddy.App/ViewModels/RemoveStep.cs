namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The pages of the "Remove this device" dialog (ADR 54, 55), in order; <see cref="Older"/> is skipped when no device would be cut off by accident.</summary>
public enum RemoveStep
{
    /// <summary>What removal does.</summary>
    What,

    /// <summary>Keep or erase its data.</summary>
    Data,

    /// <summary>Back it up first: Google Drive, a file, or not at all.</summary>
    Backup,

    /// <summary>Devices on an older version that would be cut off too.</summary>
    Older,

    /// <summary>The last look before it happens.</summary>
    Confirm,

    /// <summary>Writing the change.</summary>
    Working,

    /// <summary>The backup didn't work; the data is untouched until the user picks what to do.</summary>
    BackupProblem,

    /// <summary>What was done.</summary>
    Done,
}
