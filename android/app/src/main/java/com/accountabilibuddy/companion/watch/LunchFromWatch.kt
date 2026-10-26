package com.accountabilibuddy.companion.watch

import com.accountabilibuddy.companion.lunch.LunchStore
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.shared.LunchRemote

/**
 * The watch's Lunch page ("/lunch"): start, back early, +5 or −5 min, applied to the phone's lunch as if tapped on
 * Today. From there it goes to the other devices in the sync file and back to the watch in the next snapshot. Only
 * the linked watch is heard; a start on a day that isn't a workday, or after today's lunch, does nothing.
 */
object LunchFromWatch {
    fun apply(lunch: LunchStore, plan: DayPlan, action: String) {
        when (action) {
            LunchRemote.START -> if (lunch.canStart(plan)) lunch.start()
            LunchRemote.BACK -> lunch.endNow()
            LunchRemote.PLUS -> lunch.adjust(longer = true)
            LunchRemote.MINUS -> lunch.adjust(longer = false)
        }
    }
}
