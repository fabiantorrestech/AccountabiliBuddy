namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>Where a copy of the account's Tasks settings offered by the setup assistant and Settings (M7) comes from.</summary>
public enum TaskAccountSettingsOrigin
{
    /// <summary>The copy the account holds now (synced from the user's devices).</summary>
    Account,

    /// <summary>A backup file's copy.</summary>
    Backup,

    /// <summary>A fresh start: the built-in Default view, no picked calendar.</summary>
    Fresh,
}
