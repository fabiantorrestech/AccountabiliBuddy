namespace AccountabiliBuddy.App.Services;

/// <summary>Tokens from Google sign-in. Only <paramref name="RefreshToken"/> is stored (encrypted); access tokens live in memory.</summary>
public sealed record GoogleTokens(string RefreshToken, string AccessToken, DateTimeOffset AccessTokenExpiresAt);
