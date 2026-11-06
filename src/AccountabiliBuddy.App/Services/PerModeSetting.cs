using System.Text.Json.Nodes;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// One setting kept per mode (ADR 69), as the compare view lists it: where it lives (<paramref name="DocumentKey"/>, the
/// AccountabiliBuddy document; ProductaBuddy's is the same key with <see cref="ModeSettings.CompanionSuffix"/>), its JSON path in
/// that document, the Settings section and label it shows under, and how its value reads.
/// </summary>
public sealed record PerModeSetting(string DocumentKey, string Path, string Section, string Label, Func<JsonNode?, string> Format);
