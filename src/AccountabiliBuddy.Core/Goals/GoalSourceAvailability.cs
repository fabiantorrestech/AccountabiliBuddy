using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Goals;

/// <summary>Where a source can be measured today (<see cref="GoalSources.AvailabilityOf"/>), for the greyed and "Measured on" texts.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoalSourceAvailability>))]
public enum GoalSourceAvailability
{
    /// <summary>Counted the same on every device from what it already holds.</summary>
    Everywhere,

    /// <summary>Counted on the PC only; the phone shows "Measured on your PC".</summary>
    PcOnly,

    /// <summary>Counted on the phone only; the PC shows "Measured on your phone" (until ADR 79 lets the derived day value travel).</summary>
    PhoneOnly,

    /// <summary>Drawn greyed, "Coming soon", not clickable: it needs an ADR that isn't accepted yet.</summary>
    ComingSoon,
}
