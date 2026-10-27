namespace AccountabiliBuddy.Relay;

/// <summary>The fixed limits that bound abuse of a relay that may face the internet (ADR 46).</summary>
internal static class RelayLimits
{
    public const int MaxFileBytes = 4 * 1024 * 1024;
    public const int MaxFilesPerGroup = 64;

    /// <summary>Trust-on-first-use lets anyone claim a new group id, so the number of groups is capped too.</summary>
    public const int MaxGroups = 1000;

    public const int MaxStreamsPerIp = 8;
    public const int MaxConnectionsPerIp = 32;
    public const int MaxNewGroupsPerIpPerDay = 5;
    public const int DefaultMaxDiskMegabytes = 2048;
    public const int MaxStreamsPerGroup = 16;
    public const int MaxStreamsTotal = 256;
    public const int RequestsPerMinutePerIp = 120;
    public static readonly TimeSpan MaxFileAge = TimeSpan.FromDays(30);
    public static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    public static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(25);
}
