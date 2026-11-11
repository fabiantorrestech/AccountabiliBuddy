using System.Text.Json;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The reason Google gives for a refused API call (its error body's <c>error.errors[0].reason</c>), carried on the
/// <see cref="HttpRequestException"/> that <see cref="CalendarNetwork"/> throws, so a 403 for a rate limit, a missing scope or a
/// calendar the app may not change can be told apart (ADR 78). Holds Google's code word only, never the request.
/// </summary>
public static class GoogleErrorReason
{
    public const string DataKey = "GoogleReason";

    /// <summary>The reason in an error body; null when it has none.</summary>
    public static string? FromBody(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out JsonElement error)
                   && error.ValueKind == JsonValueKind.Object
                   && error.TryGetProperty("errors", out JsonElement errors)
                   && errors.ValueKind == JsonValueKind.Array
                   && errors.GetArrayLength() > 0
                   && errors[0].TryGetProperty("reason", out JsonElement reason)
                   && reason.ValueKind == JsonValueKind.String
                ? reason.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Puts the body's reason on the exception (when it has one).</summary>
    public static HttpRequestException With(HttpRequestException exception, string body)
    {
        if (FromBody(body) is { } reason)
        {
            exception.Data[DataKey] = reason;
        }

        return exception;
    }

    public static string? Of(Exception exception)
    {
        return exception.Data[DataKey] as string;
    }

    /// <summary>Too many calls: wait and try again.</summary>
    public static bool IsRateLimit(Exception exception)
    {
        return Of(exception) is "rateLimitExceeded" or "userRateLimitExceeded" or "quotaExceeded" or "dailyLimitExceeded";
    }

    /// <summary>The sign-in doesn't hold the scope the call needs (consent missing or taken back).</summary>
    public static bool IsMissingScope(Exception exception)
    {
        return Of(exception) is "insufficientPermissions" or "ACCESS_TOKEN_SCOPE_INSUFFICIENT";
    }
}
