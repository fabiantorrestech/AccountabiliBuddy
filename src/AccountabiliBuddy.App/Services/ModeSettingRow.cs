using System.Text.Json.Nodes;

namespace AccountabiliBuddy.App.Services;

/// <summary>A per-mode setting whose two values differ (ADR 69): AccountabiliBuddy's and ProductaBuddy's, raw and as read.</summary>
public sealed record ModeSettingRow(PerModeSetting Setting, JsonNode? Guarding, JsonNode? Companion)
{
    public string GuardingText => Read(Guarding);

    public string CompanionText => Read(Companion);

    // A value of an unexpected shape reads as its JSON rather than failing the whole view.
    private string Read(JsonNode? value)
    {
        try
        {
            return Setting.Format(value);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return value?.ToJsonString() ?? "Default";
        }
    }
}
