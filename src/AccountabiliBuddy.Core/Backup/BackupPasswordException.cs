namespace AccountabiliBuddy.Core.Backup;

/// <summary>The backup needs a password, or the one given is wrong (or the file was changed).</summary>
public sealed class BackupPasswordException : Exception
{
    public BackupPasswordException(string message)
        : base(message)
    {
    }

    public BackupPasswordException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
