using AccountabiliBuddy.Core.Domain;
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
    private const int CommsSchemaVersion = 6;
    private const int PreWorkSchemaVersion = 7;

    // v8: flexible breaks (a part's FlexMinutes and HintAt, optional): nothing to migrate, older plans have none.
    // v9: a Sleep part's WakeByAlarm (optional, ADR 33): nothing to migrate, older plans wake at the part's end.
    // v10: a day template's Blocks (optional, ADR 37): nothing to migrate, older plans follow BlockRules.Default.

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
        if (version >= DayPlanSchemaVersion && version < CommsSchemaVersion)
        {
            // v6: comms windows (an untouched starting plan gets them; an edited plan the category only).
            document = document with { DayPlan = CommsMigration.Upgrade(document.DayPlan) };
        }

        if (version >= DayPlanSchemaVersion && version < PreWorkSchemaVersion)
        {
            // v7: the Pre-Work category (ADR 31), no parts.
            document = document with { DayPlan = PreWorkMigration.Upgrade(document.DayPlan) };
        }

        return version < ProfileDocument.CurrentSchemaVersion
            ? document with { SchemaVersion = ProfileDocument.CurrentSchemaVersion }
            : document;
    }

    /// <summary>
    /// The day plan alone, in exactly the shape it has inside the document (the same options), e.g. to send to a paired
    /// device or receive one back (ADR 19).
    /// </summary>
    public static JsonElement SerializeDayPlan(DayPlan plan)
    {
        return JsonSerializer.SerializeToElement(plan, Options);
    }

    /// <summary>
    /// A day plan from another device (ADR 19), checked for shape: every list present, no null items, and only known
    /// enum values (an unknown number would be saved and then break every device that reads the document).
    /// </summary>
    /// <exception cref="JsonException">The JSON isn't a complete day plan.</exception>
    public static DayPlan DeserializeDayPlan(JsonElement json)
    {
        DayPlan plan;
        try
        {
            plan = json.Deserialize<DayPlan>(Options) ?? throw new JsonException("The day plan is empty.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            // E.g. no JSON at all (an empty element), or a value of the wrong kind.
            throw new JsonException("The day plan can't be read.", ex);
        }

        // Missing lists and null items come through as null from the records' constructors; a plan needs them all.
        if (plan.Categories is null || plan.Workdays is null || plan.Workday?.Parts is null || plan.OtherDay?.Parts is null
            || plan.OneOffDays is null
            || plan.Categories.Any(category => category is null || category.Name is null || category.Color is null)
            || plan.Workday.Parts.Concat(plan.OtherDay.Parts).Any(part => part is null)
            || plan.OneOffDays.Any(day => day?.Template?.Parts is null || day.Template.Parts.Any(part => part is null)))
        {
            throw new JsonException("The day plan is incomplete.");
        }

        if (plan.Categories.Any(category => !Enum.IsDefined(category.Behavior)) || plan.Workdays.Any(day => !Enum.IsDefined(day)))
        {
            throw new JsonException("The day plan has a value this version doesn't know.");
        }

        return plan;
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
