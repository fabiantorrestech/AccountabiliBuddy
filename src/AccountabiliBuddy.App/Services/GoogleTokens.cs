namespace AccountabiliBuddy.App.Services;

/// <summary>Tokens from Google sign-in. Only <paramref name="RefreshToken"/> is stored (encrypted); access tokens live in memory.</summary>
/// <param name="Scopes">The scopes Google granted (space-separated), when it said; ADR 78 checks the export scope with it.</param>
public sealed record GoogleTokens(string RefreshToken, string AccessToken, DateTimeOffset AccessTokenExpiresAt, string? Scopes = null);
