namespace AccountabiliBuddy.Core.Storage;

/// <summary>The database was written by a newer version of the app (e.g. an older build was installed over a newer one).
/// It is not opened, so this version can't damage what it doesn't understand.</summary>
public sealed class DatabaseTooNewException : Exception
{
    public DatabaseTooNewException(long databaseVersion, long supportedVersion)
        : base($"The data was saved by a newer version of AccountabiliBuddy (database version {databaseVersion}; this version understands up to {supportedVersion}). Install the newer version again.")
    {
        DatabaseVersion = databaseVersion;
        SupportedVersion = supportedVersion;
    }

    public long DatabaseVersion { get; }

    public long SupportedVersion { get; }
}
