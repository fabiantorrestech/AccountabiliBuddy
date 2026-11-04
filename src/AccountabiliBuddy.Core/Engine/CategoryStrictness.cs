using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>How strict a kind of day plan time is for the guards (ADR 58): Watch 3, Comms 2, Rest 1, anything else 0.</summary>
public static class CategoryStrictness
{
    public static int Of(CategoryBehavior behavior)
    {
        return behavior switch
        {
            CategoryBehavior.Watch => 3,
            CategoryBehavior.Comms => 2,
            CategoryBehavior.Rest => 1,
            _ => 0,
        };
    }

    /// <summary>
    /// Which part keeps the time when day plan parts overlap (ADR 63): the lower rank, so the weaker rules win. Rest 1, Comms 2, Work 3,
    /// Sleep 4, Pre-Work 5, from the order the calendar overlay's base goes by. The phone's <c>CategoryStrictness.overlapRank</c> is the same.
    /// </summary>
    public static int OverlapRank(CategoryBehavior behavior)
    {
        return behavior switch
        {
            CategoryBehavior.Rest => 1,
            CategoryBehavior.Comms => 2,
            CategoryBehavior.Watch => 3,
            CategoryBehavior.Sleep => 4,
            _ => 5,
        };
    }
}
