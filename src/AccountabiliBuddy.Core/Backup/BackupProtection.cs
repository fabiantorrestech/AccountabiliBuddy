namespace AccountabiliBuddy.Core.Backup;

/// <summary>How a backup file is protected.</summary>
public enum BackupProtection
{
    /// <summary>Readable JSON; used for backups without secrets.</summary>
    None,

    /// <summary>Encrypted with a password the user chose (AES-GCM, key from PBKDF2-SHA256); opens on any computer.</summary>
    Password,

    /// <summary>Encrypted for this Windows account on this computer (DPAPI); used for automatic backups before a restore.</summary>
    ThisComputer,
}
