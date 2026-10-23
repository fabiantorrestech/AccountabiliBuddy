namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>The Drive sign-in kept on this PC (document "sync.drive"): the refresh token DPAPI-sealed, and when.</summary>
public sealed record DriveAccountState(string? ProtectedRefreshToken, DateTimeOffset? SignedInAt);
