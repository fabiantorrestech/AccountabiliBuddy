using System.Text.Json;
using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Storage;

/// <summary>
/// Reads and writes <see cref="ProfileDocument"/> as JSON: camelCase names, enums as strings,
/// durations as "hh:mm:ss" and times of day as "HH:mm:ss" so other languages can parse them.
/// </summary>
public static class ProfileDocumentSerializer
{
    private const string SchemaVersionProperty = "schemaVersion";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(ProfileDocument document)
    {
        return JsonSerializer.Serialize(document, Options);
    }

    /// <exception cref="UnsupportedSchemaVersionException">The document is from a newer app version.</exception>
    /// <exception cref="JsonException">The JSON is malformed or has no schema version.</exception>
    public static ProfileDocument Deserialize(string json)
    {
        int version = ReadSchemaVersion(json);
        if (version > ProfileDocument.CurrentSchemaVersion)
        {
            throw new UnsupportedSchemaVersionException(version, ProfileDocument.CurrentSchemaVersion);
        }

        ProfileDocument document = JsonSerializer.Deserialize<ProfileDocument>(json, Options)
            ?? throw new JsonException("Profile document is empty.");

        // Older versions: the fields added since are absent and take their defaults (v2: no unlock options, no off
        // days, relock at midnight; v3: looser changes wait 10 minutes, ask about unlisted sites); the document is
        // written back in the current version on the next save.
        return version < ProfileDocument.CurrentSchemaVersion
            ? document with { SchemaVersion = ProfileDocument.CurrentSchemaVersion }
            : document;
    }

    private static int ReadSchemaVersion(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty(SchemaVersionProperty, out JsonElement versionElement)
            || !versionElement.TryGetInt32(out int version))
        {
            throw new JsonException($"Profile document has no integer '{SchemaVersionProperty}'.");
        }

        return version;
    }
}
