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

    /// <summary>Counted on the phone only; the PC shows "Measured on your phone" and takes the phone's synced day value (ADR 79). Health Connect steps and sleep; screen time is <see cref="Everywhere"/> since ST1.</summary>
    PhoneOnly,

    /// <summary>Drawn greyed, "Coming soon", not clickable: it needs an ADR that isn't accepted yet.</summary>
    ComingSoon,
}
