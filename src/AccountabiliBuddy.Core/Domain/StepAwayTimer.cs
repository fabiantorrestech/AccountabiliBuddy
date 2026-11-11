using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>"When I step away" (the owner's decision of 2026-11-11, ADR 77): what a step away does to the session timer.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StepAwayTimer>))]
public enum StepAwayTimer
{
    /// <summary>The default: the timer keeps running, phases end on time, blocking stays on; the time is logged as stepped away.</summary>
    KeepTimerRunning,

    /// <summary>The old step away: the countdown pauses and blocking holds as it was until "I'm back", which moves the session on by the time away.</summary>
    PauseTimer,
}
