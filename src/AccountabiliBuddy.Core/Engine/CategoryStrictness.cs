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
}
