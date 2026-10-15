namespace AccountabiliBuddy.Core.Storage;

/// <summary>Thrown when a profile document was written by a newer app version than this one understands.</summary>
public sealed class UnsupportedSchemaVersionException : Exception
{
    public UnsupportedSchemaVersionException(int foundVersion, int supportedVersion)
        : base($"Profile document schema version {foundVersion} is newer than the supported version {supportedVersion}.")
    {
        FoundVersion = foundVersion;
        SupportedVersion = supportedVersion;
    }

    public int FoundVersion { get; }

    public int SupportedVersion { get; }
}
