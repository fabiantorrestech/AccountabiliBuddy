namespace AccountabiliBuddy.App.Services.Goals;

/// <summary>How the Goals list orders the goals inside each group (Options › List › Sort; board Goals-PC).</summary>
public enum GoalListSort
{
    /// <summary>The order the goals were put in.</summary>
    YourOrder,

    /// <summary>Goals behind this period first, each group otherwise in your order.</summary>
    BehindFirst,
}
