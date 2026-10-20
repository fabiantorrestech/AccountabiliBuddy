using System.Text.Json;

namespace AccountabiliBuddy.Core.Link;

/// <summary>One message on the link: its type (see <see cref="LinkMessages"/>) and its body, if it has one.</summary>
public sealed record LinkEnvelope(string Type, JsonElement? Body)
{
    public static LinkEnvelope Of<T>(string type, T body)
    {
        return new LinkEnvelope(type, JsonSerializer.SerializeToElement(body, LinkFraming.JsonOptions));
    }

    public static LinkEnvelope Empty(string type)
    {
        return new LinkEnvelope(type, null);
    }

    /// <returns>The body as <typeparamref name="T"/>, or null when there is none or it doesn't fit.</returns>
    public T? BodyAs<T>()
        where T : class
    {
        if (Body is not JsonElement body)
        {
            return null;
        }

        try
        {
            return body.Deserialize<T>(LinkFraming.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
