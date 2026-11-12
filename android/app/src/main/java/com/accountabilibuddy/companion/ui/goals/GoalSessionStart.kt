package com.accountabilibuddy.companion.ui.goals

import com.accountabilibuddy.companion.sync.PcProfile
import com.accountabilibuddy.companion.sync.PcState

/**
 * What the goal menu's "Start a session…" needs (brief §4.3, change note §4a): the controlled PC's name and state (its focus
 * profiles), why a session can't be started from this phone now ([blocked]; null when it can), and the start itself. [onStart] is
 * the start Today's own Start button uses (ADR 42: profile id, null = the PC's default; minutes, null = the length the PC remembers;
 * count up), so nothing new is sent and the PC answers as it always does. It never goes around a wait, a lock or a running session.
 */
class GoalSessionStart(
    val pcName: String,
    val pcState: PcState?,
    val blocked: String?,
    val onStart: (profileId: String?, minutes: Int?, countUp: Boolean) -> Unit,
) {
    /** The PC's focus profiles, as its file lists them. */
    val profiles: List<PcProfile> get() = pcState?.profiles.orEmpty()

    companion object {
        /** A preset's length: the first of the lengths Today's Start offers. */
        const val PRESET_MINUTES = 25

        /**
         * Why Start isn't offered now, in the cases Today's Start button isn't either ([com.accountabilibuddy.companion.ui.canStartHere]),
         * plus a session already running; null when a session can be started.
         */
        fun blockedReason(pcName: String?, pcState: PcState?, updateNeeded: String?, waiting: Boolean, settingsRest: Boolean): String? = when {
            pcName == null || pcState == null -> "Link a PC to start sessions from here"
            pcState.isActive -> "A session is running"
            updateNeeded != null -> updateNeeded
            waiting -> "Waiting for $pcName's answer"
            settingsRest -> "Start comes back when the session ends or the PC syncs"
            else -> null
        }
    }
}
