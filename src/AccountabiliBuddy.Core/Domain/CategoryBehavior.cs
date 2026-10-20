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
}
