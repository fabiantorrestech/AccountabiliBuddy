package com.accountabilibuddy.companion.session

import com.accountabilibuddy.shared.session.PhoneClockSource
import com.accountabilibuddy.shared.session.PhoneEndSettings
import com.accountabilibuddy.shared.session.PhoneSessionEndKind
import com.accountabilibuddy.shared.session.PhoneSessionMode
import com.accountabilibuddy.shared.session.PhoneSessionRecord
import java.time.Instant
import java.time.ZoneId

/** The alarms a phone session needs: the set end, an armed end whose wait is over (R8), and a waking heartbeat while it runs (S7: about every 15 minutes, inexact). */
enum class PhoneAlarmKind { END, ARMED, HEARTBEAT }

/**
 * Everything [PhoneSessionController] needs from the app, and the things it asks the app to do. No Android type, so the checks drive the
 * controller with a fake. Effects an unbuilt part owns (the sync file: PS2b; the enforcement: PS2c) default to doing nothing.
 */
interface PhoneSessionPorts {
    val store: PhoneSessionStore
    val clockSource: PhoneClockSource

    // What it reads.
    fun mode(): PhoneSessionMode
    fun endSettings(): PhoneEndSettings
    fun zone(): ZoneId

    /** A lunch is running now (on trusted time, any device's). */
    fun lunchRunning(): Boolean

    /** The running lunch piece's recorded start; null when none. */
    fun lunchStart(): Instant?

    /** R1: another device of the group has a live session. */
    fun liveElsewhere(now: Instant): Boolean

    fun newId(): String

    // What it asks.
    /**
     * The exact alarm of [kind] in [afterMs] milliseconds on the ELAPSED clock (so moving the wall clock moves nothing); null cancels it.
     * Android drops alarms at a reboot: the controller asks again at each app start.
     */
    fun scheduleAlarm(kind: PhoneAlarmKind, afterMs: Long?)

    /** The phone's sync file should be written now (PS2b writes it; never asked for a test session). */
    fun requestSyncWrite() {}

    /** The enforcement (LockMyPhone, Curbox) is asked to hold until [endsAt] (null: no set end) (PS2c). */
    fun enforceUntil(record: PhoneSessionRecord, endsAt: Instant?) {}

    /** The session [record] ended: release what it asked for (PS2c). */
    fun releaseEnforcement(record: PhoneSessionRecord, kind: PhoneSessionEndKind) {}

    /** The state changed (saved): the notification, widget and watch follow. */
    fun changed() {}
}
