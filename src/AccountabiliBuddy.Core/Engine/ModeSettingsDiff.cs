using System.Text.Json.Nodes;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Compares and merges the two copies of a setting kept per mode (ADR 69): AccountabiliBuddy's and ProductaBuddy's documents, as
/// JSON. A path names one setting inside a document, dot-separated ("timeline.opacity"); a missing value reads as null.
/// </summary>
public static class ModeSettingsDiff
{
    /// <summary>The paths whose values differ between the two documents, in the order given.</summary>
    public static IReadOnlyList<string> DifferingPaths(JsonNode? guarding, JsonNode? companion, IEnumerable<string> paths)
    {
        return paths.Where(path => !JsonNode.DeepEquals(At(guarding, path), At(companion, path))).ToList();
    }

    /// <summary>The value at <paramref name="path"/>, or null when any part of it is missing.</summary>
    public static JsonNode? At(JsonNode? document, string path)
    {
        JsonNode? node = document;
        foreach (string part in path.Split('.'))
        {
            node = (node as JsonObject)?[part];
        }

        return node;
    }

    /// <summary>
    /// A copy of <paramref name="document"/> whose top-level names start lower case, as the documents are written today (an older
    /// document may still say "Choice"). Only the top level: deeper objects may be dictionaries keyed by app names or ids. When both
    /// spellings are there, the lower-case one wins.
    /// </summary>
    public static JsonNode? CamelCased(JsonNode? document)
    {
        if (document is not JsonObject source)
        {
            return document?.DeepClone();
        }

        var result = new JsonObject();
        foreach ((string name, JsonNode? value) in source)
        {
            string camel = name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
            if (camel == name || !source.ContainsKey(camel))
            {
                result[camel] = value?.DeepClone();
            }
        }

        return result;
    }

    /// <summary>A copy of <paramref name="document"/> with <paramref name="value"/> at <paramref name="path"/> (null removes it).</summary>
    public static JsonObject WithValue(JsonNode? document, string path, JsonNode? value)
    {
        JsonObject root = document?.DeepClone() as JsonObject ?? new JsonObject();
        string[] parts = path.Split('.');
        JsonObject current = root;
        foreach (string part in parts[..^1])
        {
            if (current[part] is not JsonObject next)
            {
                next = new JsonObject();
                current[part] = next;
            }

            current = next;
        }

        if (value is null)
        {
            current.Remove(parts[^1]);
        }
        else
        {
            current[parts[^1]] = value.DeepClone();
        }

        return root;
    }
}
