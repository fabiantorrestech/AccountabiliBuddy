namespace AccountabiliBuddy.Relay;

/// <summary>The fixed limits that bound abuse of a relay that may face the internet (ADR 46).</summary>
internal static class RelayLimits
{
    public const int MaxFileBytes = 4 * 1024 * 1024;
    public const int MaxFilesPerGroup = 64;

    /// <summary>
    /// The default for <c>--max-groups</c>: a personal relay serves a handful of groups. A cap stops a flood of new
    /// groups, and the admission key (<see cref="AdmissionKey"/>) stops strangers from making any.
    /// </summary>
    public const int DefaultMaxGroups = 16;

    /// <summary>The header that carries the admission key on the PUT that creates a group.</summary>
    public const string AdmissionHeader = "X-Relay-Admission";

    /// <summary>The longest a request body may take to arrive (a slow trickle would hold a connection slot).</summary>
    public static readonly TimeSpan BodyDeadline = TimeSpan.FromSeconds(30);

    public const int MaxStreamsPerIp = 8;
    public const int MaxConnectionsPerIp = 16;
    public const int MaxNewGroupsPerIpPerDay = 5;
    public const int DefaultMaxDiskMegabytes = 2048;
    public const int MaxStreamsPerGroup = 16;
    public const int MaxStreamsTotal = 256;
    public const int RequestsPerMinutePerIp = 120;
    public static readonly TimeSpan MaxFileAge = TimeSpan.FromDays(30);
    public static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    public static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(25);
}
