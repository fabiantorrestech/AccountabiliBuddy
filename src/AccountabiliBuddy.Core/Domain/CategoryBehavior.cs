namespace AccountabiliBuddy.Core.Domain;

/// <summary>What a day-plan category asks of the buddy (design part B).</summary>
public enum CategoryBehavior
{
    /// <summary>The buddy asks about new sites and counts drift (Work, Extra Work).</summary>
    Watch,

    /// <summary>No asking; parked "Today" items come up (Lunch, Evenings, Free time).</summary>
    Rest,

    /// <summary>Bedtime reminder and the shaded night.</summary>
    Sleep,

    /// <summary>
    /// A work part for mail and chat (schema v6): counts as planned work like <see cref="Watch"/>; on a day with such parts,
    /// mail and chat (Engine.CommsApps) are on track inside them and distracting during a session outside them.
    /// </summary>
    Comms,

    /// <summary>
    /// A guarded warm-up before work (schema v7, ADR 31): not planned work; with no session running, work tools and the
    /// default profile's distractions are minimized once the user says today is a work day.
    /// </summary>
    PreWork,
}
