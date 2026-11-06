using System.Globalization;
using System.Text.Json.Nodes;
using AccountabiliBuddy.App.Services.Theming;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The PC's settings kept per mode (ADR 69) and how the compare view shows them. Each is a device-local document that
/// AccountabiliBuddy keeps under its own key and ProductaBuddy under the key plus <see cref="ModeSettings.CompanionSuffix"/>. Only the
/// paths listed here are compared; the rest of a document (positions, a stored place) simply follows the mode.
/// </summary>
public static class PerModeSettings
{
    public const string SoundsKey = "sounds";
    public const string OverlayKey = "overlay";
    public const string HeadphonesKey = "headphones";
    public const string HyperfocusKey = "hyperfocus";
    public const string ThemeKey = "theme";
    public const string StartNudgeKey = "startNudge";

    /// <summary>
    /// ProductaBuddy's first copy of a document, from AccountabiliBuddy's (as JSON, for the compare view; the services do the same
    /// through <see cref="ModeSettings.ReadKeyBlocking"/>): a copy, except the start-of-day assistant, which starts off.
    /// </summary>
    public static JsonNode? CompanionStart(string documentKey, JsonNode? guarding) =>
        documentKey == StartNudgeKey ? Core.Engine.ModeSettingsDiff.WithValue(guarding, "assistantOn", JsonValue.Create(false)) : guarding?.DeepClone();

    /// <summary>Every per-mode document, in the order the compare view lists them.</summary>
    public static IReadOnlyList<string> DocumentKeys { get; } = [StartNudgeKey, HyperfocusKey, SoundsKey, HeadphonesKey, OverlayKey, ThemeKey];

    public static IReadOnlyList<PerModeSetting> All { get; } =
    [
        new(StartNudgeKey, "assistantOn", "Your day", "Start-of-day assistant", OnOff),
        new(HyperfocusKey, "intervalMinutes", "Focus sessions", "Hyperfocus reminder after", Minutes),
        new(HyperfocusKey, "offForProfiles", "Focus sessions", "Hyperfocus reminder off for profiles", Count),
        new(SoundsKey, "focusEndSound", "Alerts & on-screen", "When focus is up", Text),
        new(SoundsKey, "breakEndSound", "Alerts & on-screen", "When a break is up", Text),
        new(SoundsKey, "volume", "Alerts & on-screen", "Chime volume", Percent),
        new(SoundsKey, "customFile", "Alerts & on-screen", "Own sound file", Text),
        new(HeadphonesKey, "wired", "Alerts & on-screen", "Play when wired headphones connect", OnOff),
        new(HeadphonesKey, "bluetooth", "Alerts & on-screen", "Play when Bluetooth headphones connect", OnOff),
        new(HeadphonesKey, "devices", "Alerts & on-screen", "Bluetooth headphones", Count),
        new(HeadphonesKey, "onlyAtTimes", "Alerts & on-screen", "Only at certain times", OnOff),
        new(OverlayKey, "style", "Alerts & on-screen", "On-screen timer style", Enum<OverlayStyle>),
        new(OverlayKey, "size", "Alerts & on-screen", "On-screen timer size", Enum<OverlaySize>),
        new(OverlayKey, "opacity", "Alerts & on-screen", "On-screen timer opacity at rest", Percent01),
        new(OverlayKey, "hideInFullscreen", "Alerts & on-screen", "Hide while a fullscreen app is in front", OnOff),
        new(OverlayKey, "fillUp", "Alerts & on-screen", "Fill up as time passes", OnOff),
        new(OverlayKey, "quickHideSeconds", "Alerts & on-screen", "Hide button hides it for", Seconds),
        new(OverlayKey, "quiet", "Alerts & on-screen", "Quiet at rest", OnOff),
        new(OverlayKey, "showTimeline", "Alerts & on-screen", "Show the on-screen timeline", OnOff),
        new(OverlayKey, "timelineOpacity", "Alerts & on-screen", "Timeline opacity at rest", Percent01),
        new(OverlayKey, "shape", "Alerts & on-screen", "Overlay shape", Enum<OverlayShape>),
        new(OverlayKey, "progress", "Alerts & on-screen", "Overlay progress", Enum<OverlayProgress>),
        new(OverlayKey, "cornerClock", "Alerts & on-screen", "Corner clock", OnOff),
        new(OverlayKey, "showSeconds", "Alerts & on-screen", "Show seconds", OnOff),
        new(OverlayKey, "panelSize", "Alerts & on-screen", "Ctrl+Alt panel size", Enum<PanelSize>),
        new(OverlayKey, "placement", "Alerts & on-screen", "Where the overlay lives", Set),
        new(ThemeKey, "theme", "Appearance", "Theme", Text),
        new(ThemeKey, "choice", "Appearance", "Light or dark", Enum<LightDarkMode>),
        new(ThemeKey, "colour", "Appearance", "Colour", Enum<ColourMode>),
        new(ThemeKey, "darkAt", "Appearance", "Dark from", Text),
        new(ThemeKey, "lightAt", "Appearance", "Light from", Text),
    ];

    private static string OnOff(JsonNode? value) => value is null ? "Default" : value.GetValue<bool>() ? "On" : "Off";

    private static string Text(JsonNode? value) => value is null ? "Default" : value.ToString();

    private static string Minutes(JsonNode? value) => value is null ? "Default" : $"{value} min";

    private static string Seconds(JsonNode? value) => value is null ? "Default" : $"{value} s";

    private static string Percent(JsonNode? value) => value is null ? "Default" : $"{value}%";

    private static string Percent01(JsonNode? value) =>
        value is null ? "Default" : $"{Math.Round(value.GetValue<double>() * 100).ToString(CultureInfo.InvariantCulture)}%";

    private static string Count(JsonNode? value) => value is JsonArray list ? (list.Count == 0 ? "None" : $"{list.Count} chosen") : "None";

    // An object (the overlay's placement): its contents, short, so two different ones read differently.
    private static string Set(JsonNode? value)
    {
        if (value is null)
        {
            return "Default";
        }

        string text = value.ToJsonString();
        return text.Length <= 48 ? text : text[..47] + "…";
    }

    private static string Enum<T>(JsonNode? value) where T : struct, System.Enum =>
        value is null ? "Default"
        : value.GetValueKind() == System.Text.Json.JsonValueKind.Number && System.Enum.GetName(typeof(T), value.GetValue<int>()) is string name ? Words(name)
        : value.ToString();

    // "BarWithEndTime" → "Bar with end time".
    private static string Words(string name)
    {
        var text = new System.Text.StringBuilder();
        foreach (char letter in name)
        {
            if (char.IsUpper(letter) && text.Length > 0)
            {
                text.Append(' ').Append(char.ToLowerInvariant(letter));
            }
            else
            {
                text.Append(letter);
            }
        }

        return text.ToString();
    }
}
