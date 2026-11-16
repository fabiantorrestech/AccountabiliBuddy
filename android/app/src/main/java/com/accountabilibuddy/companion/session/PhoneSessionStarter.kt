package com.accountabilibuddy.companion.session

import android.content.Context
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.sync.PcFileControl
import com.accountabilibuddy.shared.session.PhoneSessionProfile
import com.accountabilibuddy.shared.session.PhoneSessionRefusal
import com.accountabilibuddy.shared.session.PhoneStartRules
import com.accountabilibuddy.shared.session.SessionTasksFromModel
import com.accountabilibuddy.shared.session.StartTarget
import com.accountabilibuddy.shared.sync.GroupSessions
import com.accountabilibuddy.shared.sync.ReceivedSyncFile

/**
 * What the Start sheet does for "On this phone" (PS3a, ADR 85): copies the chosen profile into the session ([PhoneSessionProfile.snapshot], PS2c review S3),
 * hands This session's open tasks to the controller, starts, and remembers the choice (prefs `phoneSession`: `target`, `lastProfileId`). The rules (R1, R16)
 * are [PhoneStartRules] and the controller's own refusal; this only wires them to the app.
 *
 * R5 (the request race): while a start request waits for the PC the sheet cannot be opened for "On this phone", so there is never a pending `start` action
 * to withdraw when a phone session starts here.
 */
class PhoneSessionStarter(private val app: CompanionApp) {
    /** What a start gave: the session started, or [Failed.text] says why not (the sheet shows it and stays open). */
    sealed interface Result {
        data object Started : Result
        data class Failed(val text: String) : Result
    }

    private val prefs get() = app.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    /** The remembered Start target (`pc:<deviceId>` or `phone`); null before the first start or choice. */
    fun rememberedTarget(): String? = prefs.getString(KEY_TARGET, null)

    /** The profile the last phone session used ([PhoneSessionProfile.id]); null before the first. */
    fun lastProfileId(): String? = prefs.getString(KEY_LAST_PROFILE, null)

    /** The target the sheet remembers: chosen with the segmented control, kept whether or not a session starts (R16). */
    fun remember(target: StartTarget) {
        prefs.edit().putString(KEY_TARGET, PhoneStartRules.keyOf(target)).apply()
    }

    /**
     * Starts a session on this phone: [minutes] long, or with no set end when null. A session that is already running, or a live one elsewhere (R1), is
     * refused by the controller, which also saves the checkpoint before anything else happens.
     */
    fun start(profile: PhoneSessionProfile, minutes: Int?): Result {
        // This session's open tasks at the start: the checklist "All tasks done" (R9) is judged from. Without the one task list (not migrated) there are none.
        val tasks = if (app.taskModel.isActive) SessionTasksFromModel.atStart(app.taskModel.lists.value) else emptyList()
        return when (val started = app.phoneSession.controller.start(profile.id, profile.name, minutes, tasks, isTest = false, snapshot = profile.snapshot())) {
            is PhoneSessionStart.Started -> {
                remember(StartTarget.Phone)
                prefs.edit().putString(KEY_LAST_PROFILE, profile.id).apply()
                Result.Started
            }
            PhoneSessionStart.AlreadyRunning -> Result.Failed("A session is already running on this phone.")
            PhoneSessionStart.LiveElsewhere -> Result.Failed(liveElsewhereText())
            PhoneSessionStart.StoreFailed -> Result.Failed("The session could not be saved, so it did not start. Try again.")
            is PhoneSessionStart.Refused -> Result.Failed(
                when (started.reason) {
                    PhoneSessionRefusal.BAD_LENGTH -> "A session can be 1 minute to 12 hours long."
                    else -> "The session did not start."
                },
            )
        }
    }

    // R1's refusal says where the live session is, the way Today's "Running on" bar does.
    private fun liveElsewhereText(): String {
        val now = app.phoneSession.controller.now()
        val others = app.sync.received.value.map { ReceivedSyncFile(it.file, it.receivedAt) }
        val live = GroupSessions.liveElsewhere(others, now) { PcFileControl.groupLive(it, now) }
        return live?.let { "A session is running on ${it.name}." } ?: "A session is running on another device."
    }

    private companion object {
        const val PREFS = "phoneSession"
        const val KEY_TARGET = "target"
        const val KEY_LAST_PROFILE = "lastProfileId"
    }
}
