using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// What an away does to the session timer (the owner's decisions of 2026-11-11, ADR 77): "When I step away" (a break or an
/// interruption) and "When I start a side task" each have a value of this, with the same rules (<see cref="Engine.StepAwayTimerPolicy"/>).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StepAwayTimer>))]
public enum StepAwayTimer
{
    /// <summary>The default: the timer keeps running, phases end on time, blocking stays on; the time is logged as stepped away (or side task).</summary>
    KeepTimerRunning,

    /// <summary>
    /// The old behaviour: the countdown pauses until "I'm back", which moves the session on by the time away; a step away also holds
    /// blocking as it was, a side task keeps it running. Never in a break (a break's countdown never freezes).
    /// </summary>
    PauseTimer,
}
