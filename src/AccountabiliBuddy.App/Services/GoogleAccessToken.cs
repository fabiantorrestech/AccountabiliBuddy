namespace AccountabiliBuddy.App.Services;

/// <summary>An access token from a refresh, with the scopes the sign-in holds (space-separated). Lives in memory only.</summary>
public sealed record GoogleAccessToken(string Token, DateTimeOffset ExpiresAt, string Scopes)
{
    public bool Has(string scope)
    {
        return Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope, StringComparer.Ordinal);
    }
}
