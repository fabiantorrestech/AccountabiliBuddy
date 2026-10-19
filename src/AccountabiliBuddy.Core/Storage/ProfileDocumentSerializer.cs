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
    private const int TimeOffSchemaVersion = 4;

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

        if (version < TimeOffSchemaVersion)
        {
            json = MigrateOffDaysToTimeOff(json);
        }

        ProfileDocument document = JsonSerializer.Deserialize<ProfileDocument>(json, Options)
            ?? throw new JsonException("Profile document is empty.");

        // Older versions: the fields added since are absent and take their defaults (v2: no unlock options, no off
        // days, relock at midnight; v3: looser changes wait 10 minutes, ask about unlisted sites; v4: no time off or
        // one-off blocks beyond the migrated off days); the document is written back in the current version on the
        // next save.
        return version < ProfileDocument.CurrentSchemaVersion
            ? document with { SchemaVersion = ProfileDocument.CurrentSchemaVersion }
            : document;
    }

    /// <summary>v2/v3 → v4: every off day ("schedule.offDays": ["2026-10-20"]) becomes all-day time off.</summary>
    private static string MigrateOffDaysToTimeOff(string json)
    {
        JsonNode? root = JsonNode.Parse(json);
        if (root?["schedule"] is not JsonObject schedule || schedule["offDays"] is not JsonArray offDays)
        {
            return json;
        }

        var timeOff = new JsonArray();
        foreach (JsonNode? day in offDays)
        {
            timeOff.Add(new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString(),
                ["date"] = day?.GetValue<string>(),
            });
        }

        schedule.Remove("offDays");
        schedule["timeOff"] = timeOff;
        return root.ToJsonString();
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
