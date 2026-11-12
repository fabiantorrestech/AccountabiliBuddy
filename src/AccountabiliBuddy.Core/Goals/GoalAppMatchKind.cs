using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>How one <see cref="GoalAppMatch"/> finds its usage rows (screen-time source v2, ADR 79 notes).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalAppMatchKind>))]
public enum GoalAppMatchKind
{
    /// <summary>The phone's app id (<c>com.instagram.android</c>): exact, case-sensitive, phones only.</summary>
    Package,

    /// <summary>The PC's process (<c>chrome.exe</c>): case-insensitive, ".exe" optional, PCs only.</summary>
    Process,

    /// <summary>The name the device shows (<c>YouTube</c>): case-insensitive and exact, phones and PCs.</summary>
    Name,

    /// <summary>A domain (<c>youtube.com</c>) and its subdomains, PCs only.</summary>
    Website,
}
