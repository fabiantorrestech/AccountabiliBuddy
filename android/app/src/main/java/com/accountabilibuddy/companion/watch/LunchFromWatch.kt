package com.accountabilibuddy.companion.watch

import com.accountabilibuddy.companion.guidance.TrustedTime
import com.accountabilibuddy.companion.lunch.LunchStore
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.shared.LunchRemote

/**
 * The watch's Lunch page ("/lunch"): start, back early (Count it), Discard it, Start the rest, +5 or −5 min, applied to the phone's lunch as if tapped on
 * Today. From there it goes to the other devices in the sync file and back to the watch in the next snapshot. Only
 * the linked watch is heard; a start on a day that isn't a workday, or after today's lunch, does nothing.
 */
object LunchFromWatch {
    /** True when the phone did what was asked; false when it refused (the caller then sends the watch the phone's real state). */
    fun apply(lunch: LunchStore, plan: DayPlan, action: String, discardAllowed: Boolean): Boolean {
        val now = TrustedTime.now()
        return when (action) {
            LunchRemote.START -> lunch.canStart(plan, now).also { if (it) lunch.start(now) }
            LunchRemote.BACK, LunchRemote.COUNT -> lunch.count(now)
            // The mode's setting is read here, on the phone: a watch that is out of date can never discard against it.
            LunchRemote.DISCARD -> lunch.discard(now, discardAllowed)
            LunchRemote.REST -> lunch.canStartRest(now) && lunch.startRest(now)
            LunchRemote.PLUS -> true.also { lunch.adjust(longer = true, now = now) }
            LunchRemote.MINUS -> true.also { lunch.adjust(longer = false, now = now) }
            else -> false
        }
    }
}
