package com.accountabilibuddy.companion.engine

import com.accountabilibuddy.companion.model.CategoryBehavior

/** How strict a kind of day plan time is for the guards (ADR 58): Watch 3, Comms 2, Rest 1, anything else 0. The PC's CategoryStrictness. */
object CategoryStrictness {
    fun of(behavior: CategoryBehavior): Int = when (behavior) {
        CategoryBehavior.Watch -> 3
        CategoryBehavior.Comms -> 2
        CategoryBehavior.Rest -> 1
        else -> 0
    }

    /**
     * Which part keeps the time when day plan parts overlap (ADR 63): the lower rank, so the weaker rules win. Rest 1, Comms 2, Work 3,
     * Sleep 4, Pre-Work 5. The PC's CategoryStrictness.OverlapRank.
     */
    fun overlapRank(behavior: CategoryBehavior): Int = when (behavior) {
        CategoryBehavior.Rest -> 1
        CategoryBehavior.Comms -> 2
        CategoryBehavior.Watch -> 3
        CategoryBehavior.Sleep -> 4
        else -> 5
    }
}
