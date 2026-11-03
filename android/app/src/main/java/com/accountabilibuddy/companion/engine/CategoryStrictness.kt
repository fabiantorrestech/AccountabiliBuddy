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
}
