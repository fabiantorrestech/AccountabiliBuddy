package com.accountabilibuddy.companion.goals

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.shared.goals.GoalPhoneDerived
import com.accountabilibuddy.shared.goals.GoalScreenTimeDerived
import com.accountabilibuddy.companion.guidance.TrustedTime
import java.time.LocalDate
import java.time.ZoneId

/**
 * Writes the values this phone measures as goal entries (ADR 79, GS4): Health Connect steps while "Track daily steps" is on, and sleep once the
 * user granted it, for the goals that count them ([GoalPhoneDerived], [PhoneGoalStore.writeDerived]). Entries carry this phone's device id; the
 * phone changes only its own, and only when a value changed. Screen time (ST3): the whole minutes of this phone's usage in the apps a goal's screen-time source names, for the goals that count on this phone ([GoalScreenTimeDerived], read by [PhoneScreenTime]; no Usage access = nothing written or removed). Called after a Health Connect read and on every refresh. "Today" is the trusted clock's (ADR 70), never the phone's own.
 */
object PhoneGoalDerived {
    fun refresh(app: CompanionApp) {
        if (app.goals.status.value != PhoneGoalStatus.Ready) return
        val steps = app.steps.state.value
        val days = if (steps.enabled) steps.days.map { it.date to it.count.toLong() } else emptyList()
        val goals = app.goals.lists.value.liveGoals
        val result = GoalPhoneDerived.readings(goals, days, PhoneGoalData.slept(app), LocalDate.ofInstant(TrustedTime.now(), ZoneId.systemDefault()))
        // Screen time is read only when a goal counts it on this phone (no read, no Usage access query, otherwise).
        val screen = if (GoalScreenTimeDerived.counting(goals, app.sync.deviceId).isEmpty()) null
        else GoalScreenTimeDerived.readings(goals, app.sync.deviceId, PhoneScreenTime.rowsByDay(app), LocalDate.ofInstant(TrustedTime.now(), ZoneId.systemDefault()), app.goals.lists.value.entries)
        app.goals.writeDerived(app.sync.deviceId, result.readings + (screen?.readings ?: emptyList()), result.covered + (screen?.covered ?: emptyList()))
    }
}
