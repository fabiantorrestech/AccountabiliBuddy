namespace AccountabiliBuddy.App.Services.Beeminder;

/// <summary>What one <see cref="BeeminderService.RefreshAsync"/> did, so Test can say what really happened.</summary>
public enum BeeminderRefresh
{
    /// <summary>Beeminder answered and the goals were read.</summary>
    Read,

    /// <summary>No token is saved: nothing was asked.</summary>
    NoToken,

    /// <summary>Another read was already under way: nothing new was asked.</summary>
    Busy,

    /// <summary>Beeminder asked to wait (rate limit) or rejected the token; nothing was asked.</summary>
    Waiting,

    /// <summary>Asked, or tried to, and it failed; <see cref="BeeminderService.Problem"/> says why.</summary>
    Failed,
}
