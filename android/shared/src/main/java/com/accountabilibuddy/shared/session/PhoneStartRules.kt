package com.accountabilibuddy.shared.session

import com.accountabilibuddy.shared.sync.GroupSessionLive
import com.accountabilibuddy.shared.sync.GroupSessionStale
import com.accountabilibuddy.shared.sync.GroupSessions
import java.time.Duration
import java.time.Instant

/** Where a session starts from the phone's Start sheet (R16). */
sealed interface StartTarget {
    /** On the controlled PC (ADR 42, through the sync files). [deviceId] is that PC's sync device id. */
    data class Pc(val deviceId: String) : StartTarget

    /** On this phone (ADR 85). */
    data object Phone : StartTarget
}

/**
 * The controlled PC as the Start sheet sees it. [online] = its file is at most 15 minutes old, its app answers control, and it has no live session
 * ([PhoneStartRules.pcOnline]); [lastHeardAt] is when its file was last heard from, for "isn't online (last heard 2:05 PM)".
 */
data class StartPc(val deviceId: String, val name: String, val online: Boolean, val lastHeardAt: Instant?)

/** Why a session can't be started from this phone now (R1, R5, B15); the text is what Today and the goal menu say. */
sealed interface StartBlock {
    val text: String

    /** R1: a live session on another device of the group: "Running on Pixel · 32 min left". */
    data class RunningElsewhere(val live: GroupSessionLive, override val text: String) : StartBlock

    /** B15: a start request still waits for the PC's answer; "On this phone" stays off until it is cancelled or answered. */
    data class WaitingForPc(val pcName: String) : StartBlock {
        override val text: String get() = "Waiting for $pcName's answer"
    }

    /** ADR 60: the settings rest for a PC session whose file is out of date. */
    data object SettingsRest : StartBlock {
        override val text: String get() = "Start comes back when the session ends or the PC syncs"
    }

    /** A session of the phone's own already runs (never a second one). */
    data object OwnRunning : StartBlock {
        override val text: String get() = "A session is running"
    }
}

/** What the Start sheet shows and does before anything is sent (R1, R5, R15, R16, plan check B15). Pure; the app passes trusted times. */
object PhoneStartRules {
    /** The remembered choice for "On this phone" (prefs `phoneSession`, key `target`). */
    const val PHONE_KEY = "phone"

    private const val PC_PREFIX = "pc:"

    /** A PC's file counts as "online" while it is no older than this (the group's quiet limit). */
    val ONLINE_WITHIN: Duration = GroupSessions.QUIET_AFTER

    fun keyOf(target: StartTarget): String = when (target) {
        StartTarget.Phone -> PHONE_KEY
        is StartTarget.Pc -> PC_PREFIX + target.deviceId
    }

    /** The target a saved key names; null for nothing saved or text this version doesn't know. */
    fun parse(key: String?): StartTarget? = when {
        key == null -> null
        key == PHONE_KEY -> StartTarget.Phone
        key.startsWith(PC_PREFIX) && key.length > PC_PREFIX.length -> StartTarget.Pc(key.substring(PC_PREFIX.length))
        else -> null
    }

    /**
     * Whether the controlled PC can take a start now: its file was heard within 15 minutes ([heardAt] is [GroupSessions.heardAt], never ahead of the
     * arrival), its app answers control ([supportsControl]), and no live session runs on it ([liveSession]).
     */
    fun pcOnline(heardAt: Instant, now: Instant, supportsControl: Boolean, liveSession: Boolean): Boolean =
        supportsControl && !liveSession && Duration.between(heardAt, now) <= ONLINE_WITHIN

    /**
     * R16, the target the sheet opens on. The remembered choice wins when it is available ("this phone" always is; a PC only while it is the controlled
     * one and online). First time, or when the remembered PC is not available: the controlled PC when online, else this phone. With no group or no PC
     * (R15) it is always this phone.
     */
    fun defaultTarget(remembered: String?, pc: StartPc?): StartTarget {
        val saved = parse(remembered)
        if (saved == StartTarget.Phone) return StartTarget.Phone
        if (saved is StartTarget.Pc && pc != null && pc.online && pc.deviceId == saved.deviceId) return saved
        return if (pc != null && pc.online) StartTarget.Pc(pc.deviceId) else StartTarget.Phone
    }

    /** The muted line under the PC option when it can't take a start: "Work PC isn't online (last heard 2:05 PM)". Null when it can, or there is no PC. */
    fun pcNote(pc: StartPc?, time: (Instant) -> String): String? {
        if (pc == null || pc.online) return null
        val heard = pc.lastHeardAt?.let { " (last heard ${time(it)})" }.orEmpty()
        return "${pc.name} isn't online$heard"
    }

    /** What the sheet's target control offers: the PC option only with a PC in the group (R15: alone, only "On this phone"). */
    fun offersPc(pc: StartPc?): Boolean = pc != null

    /**
     * Why Start can't be offered now, or null. R1 (a live session elsewhere, including a PC's when its file says so) and its own session come first,
     * then B15's waiting request, then ADR 60's settings rest. A test session never blocks (it is never counted or synced).
     */
    fun block(ownRunning: Boolean, live: GroupSessionLive?, now: Instant, waitingFor: String?, settingsRest: Boolean): StartBlock? = when {
        ownRunning -> StartBlock.OwnRunning
        live != null -> StartBlock.RunningElsewhere(live, runningOnText(live, now))
        waitingFor != null -> StartBlock.WaitingForPc(waitingFor)
        settingsRest -> StartBlock.SettingsRest
        else -> null
    }

    /**
     * R2's text: "Running on Pixel · 32 min left"; "· paused" while its timer is paused; "· no set end" for a session without one. Whole minutes
     * left, rounded down as Today's session card does; the last minute says "less than a minute left".
     */
    fun runningOnText(live: GroupSessionLive, now: Instant): String {
        val tail = when {
            live.paused -> "paused"
            live.noSetEnd || live.endsAt == null -> "no set end"
            else -> leftText(Duration.between(now, live.endsAt))
        }
        return "Running on ${live.name} · $tail"
    }

    /** R3: the muted line Start comes back with: "Pixel's session last heard from 2:05 PM". */
    fun staleText(stale: GroupSessionStale, time: (Instant) -> String): String = "${stale.name}'s session last heard from ${time(stale.lastHeardAt)}"

    private fun leftText(left: Duration): String {
        val minutes = left.seconds.coerceAtLeast(0) / 60
        return if (minutes >= 1) "$minutes min left" else "less than a minute left"
    }
}
