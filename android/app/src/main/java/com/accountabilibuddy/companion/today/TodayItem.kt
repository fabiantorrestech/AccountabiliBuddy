package com.accountabilibuddy.companion.today

import com.accountabilibuddy.companion.today.TodayPlacement.Above
import com.accountabilibuddy.companion.today.TodayPlacement.Fold
import com.accountabilibuddy.companion.today.TodayPlacement.Hidden

/**
 * The parts of the phone's Today the person can arrange (Settings › Appearance › Today layout). The declaration order is Today's
 * order while nothing was chosen, with nothing running. [defaultPlacement] is where the part sits today; a part added in a later
 * version takes its default place, so it never vanishes for a saved choice.
 *
 * [sessionDefault] is where it sits while a session runs when that differs (boards B-Phone-Session, Today-Layout-Phone: Today's goals,
 * Next up and Deep work are More today chips in a session, Daily steps is hidden there).
 *
 * [allowed] is the safety rule: a part that carries a guard or an obligation (the guard banner, the lunch chip, the "link a PC"
 * card, "Need it for work", "Not working today", the import card) is never [Hidden]; the first three are never folded either. What a running session
 * needs to say (its header, timer, wait, task list) is not in this list at all: it is always drawn first.
 */
enum class TodayItem(
    val key: String,
    val title: String,
    val about: String,
    val defaultPlacement: TodayPlacement,
    val allowed: Set<TodayPlacement> = setOf(Above, Fold, Hidden),
    val sessionDefault: TodayPlacement? = null,
) {
    Guard("guard", "Guard banner", "Says why the settings rest during a session", Above, setOf(Above)),
    LunchChip("lunch-chip", "Lunch chip", "Start lunch, or I'm back; in a session also the Budget pace", Above, setOf(Above)),
    PcMissing("pc-missing", "Link a PC", "Shown until a PC's file reaches this phone", Above, setOf(Above)),
    Goals("goals", "Today's goals", "Check-ins and logs", Above, sessionDefault = Fold),
    NextUp("next-up", "Next up", "The next parts of today's plan", Above, sessionDefault = Fold),
    Ring("ring", "Deep work", "Today's focus against your goal", Above, sessionDefault = Fold),
    Comebacks("comebacks", "Come back to", "Parked thoughts you can tick off", Fold),
    SmallCards("small", "Timer and phone time", "The nearest timer or alarm, and today's time on the phone", Above),
    Noise("noise", "Noise player", "Shown once a sound folder is set", Above),
    DailySteps("daily-steps", "Daily steps", "The step ring; a steps goal is part of Today's goals", Above, sessionDefault = Hidden),
    DayBar("day-bar", "Day bar", "The timeline with its legend and sleep line", Fold),
    WorkUse("work-use", "Need it for work", "Use the phone for work during a session", Fold, setOf(Above, Fold)),
    LunchCard("lunch-card", "Lunch", "The countdown, +5 / −5 and your other devices", Fold),
    Beeminder("beeminder", "Beeminder goals", "From a PC's sync file", Fold),
    Timers("timers", "Timers and alarms", "All of them, with their controls", Fold),
    DayOff("day-off", "Not working today", "The note, with Undo", Fold, setOf(Above, Fold)),
    Import("import", "Bring your PC's profiles", "Shown until this phone has a plan", Fold, setOf(Above, Fold)),
    TestSession("test-session", "Test session", "Try a short session; not offered during a real one", Fold),
    ;

    /** True for a part the person can never hide (it carries a guard or an obligation). */
    val alwaysShown: Boolean get() = Hidden !in allowed

    /** True for a part that can sit in the fold. */
    val canFold: Boolean get() = Fold in allowed

    /** Where the part sits while nothing was chosen: [sessionDefault] during a session when it has one, else [defaultPlacement]. */
    fun defaultFor(inSession: Boolean): TodayPlacement = if (inSession) sessionDefault ?: defaultPlacement else defaultPlacement

    /** [placement] when this part may sit there, else its default place. */
    fun clamp(placement: TodayPlacement): TodayPlacement = if (placement in allowed) placement else defaultPlacement

    companion object {
        fun fromKey(key: String): TodayItem? = entries.firstOrNull { it.key == key }
    }
}
