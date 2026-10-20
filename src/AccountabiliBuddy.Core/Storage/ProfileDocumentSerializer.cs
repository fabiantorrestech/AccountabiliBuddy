using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Storage;

/// <summary>
/// Reads and writes <see cref="ProfileDocument"/> as JSON: camelCase names, enums as strings,
/// durations as "hh:mm:ss" and times of day as "HH:mm:ss" so other languages can parse them.
/// </summary>
public static class ProfileDocumentSerializer
{
    private const string SchemaVersionProperty = "schemaVersion";
    private const int DayPlanSchemaVersion = 5;

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

        if (version < DayPlanSchemaVersion)
        {
            // Off days (v2/v3) became time off in v4, and all of the v4 schedule goes in v5; nothing to carry over.
            json = MigrateScheduleToDayPlan(json);
        }

        ProfileDocument document = JsonSerializer.Deserialize<ProfileDocument>(json, Options)
            ?? throw new JsonException("Profile document is empty.");

        // Older versions: the fields added since are absent and take their defaults (v2: no unlock options, relock at
        // midnight; v3: looser changes wait 10 minutes, ask about unlisted sites; v5: the starting day plan); the
        // document is written back in the current version on the next save.
        return version < ProfileDocument.CurrentSchemaVersion
            ? document with { SchemaVersion = ProfileDocument.CurrentSchemaVersion }
            : document;
    }

    /// <summary>False for a document from before day plans (v4 and older): it gets the starting plan when read.</summary>
    /// <exception cref="JsonException">The JSON is malformed or has no schema version.</exception>
    public static bool HasDayPlan(string json)
    {
        return ReadSchemaVersion(json) >= DayPlanSchemaVersion;
    }

    /// <summary>
    /// v4 → v5 (ADR 18): the weekly schedule of profiles, its time off and one-off blocks are dropped (blockers such as
    /// Cold Turkey keep their own schedules); the document gets the starting day plan, its default.
    /// </summary>
    private static string MigrateScheduleToDayPlan(string json)
    {
        JsonNode? root = JsonNode.Parse(json);
        if (root is not JsonObject document || !document.Remove("schedule"))
        {
            return json;
        }

        return document.ToJsonString();
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
