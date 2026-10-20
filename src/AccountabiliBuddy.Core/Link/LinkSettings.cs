namespace AccountabiliBuddy.Core.Link;

/// <summary>Whether devices may link to this PC (off until turned on in Setup), and the port it listens on.</summary>
public sealed record LinkSettings(bool Enabled, int Port)
{
    public const int DefaultPort = 47821;

    public static LinkSettings Default { get; } = new(false, DefaultPort);
}
