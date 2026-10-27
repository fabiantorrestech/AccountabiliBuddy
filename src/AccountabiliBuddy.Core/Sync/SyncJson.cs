using System.Text.Json;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>The JSON shape of sync files and of the documents in them (camelCase names), shared with the phone.</summary>
public static class SyncJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);
}
