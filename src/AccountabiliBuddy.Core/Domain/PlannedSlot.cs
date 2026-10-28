using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// The day part a task was put in the plan for (ADR 50, "Put in plan"): the date and the part's own id
/// (<see cref="PlanPart.Id"/>, stable across edits of the plan), plus the part's start time as it was when the task was
/// put there, so the task still comes back at the right moment when the part has been edited or deleted since.
/// </summary>
/// <param name="Date">The local date of the part.</param>
/// <param name="PartId">The plan part's id.</param>
/// <param name="Start">The part's start as it was chosen, local wall-clock time.</param>
public sealed record PlannedSlot(DateOnly Date, Guid PartId, TimeOnly Start)
{
    /// <summary>A slot with the start cut to whole seconds, the form both devices write and compare.</summary>
    public static PlannedSlot At(DateOnly date, Guid partId, TimeOnly start)
    {
        return new PlannedSlot(date, partId, new TimeOnly(start.Hour, start.Minute, start.Second));
    }

    /// <summary>The moment the part starts, as a local wall-clock time.</summary>
    [JsonIgnore]
    public DateTime StartsAt => Date.ToDateTime(Start);

    /// <summary>The slot as one string, in the form the phone's Kotlin twin builds from the sync file's text, for a merge tie-break (never shown or logged).</summary>
    [JsonIgnore]
    public string TieText => string.Join(
        "\u0003",
        Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        PartId.ToString("D"),
        Start.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
}
