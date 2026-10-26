package com.accountabilibuddy.companion.essentials

import android.content.Context
import android.content.Intent
import android.util.Log
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.nudge.Takeover
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import java.time.Duration
import java.time.Instant

/**
 * When the essentials screen comes up by itself, and the LockMyPhone-Extended "hardcore" profile.
 * - Unlocking during emergency phone time, or during a session on another device (Settings switch, on by default),
 *   lands on the essentials screen.
 * - Hardcore: when a session starts on another device, LockMyPhone-Extended's public intent API starts the named
 *   profile for the session's length (that app enforces; this one only asks it).
 */
object EssentialsLaunch {
    private const val TAG = "Essentials"
    /** LockMyPhone-Extended's package. */
    const val LOCK_MY_PHONE = "com.fabiantorrestech.lockmyphone_extended"
    private const val LMP_PACKAGE = LOCK_MY_PHONE
    private const val LMP_START = "$LMP_PACKAGE.action.START_LOCK"
    private const val PREFS = "essentials-launch"
    private const val KEY_HARDCORE_FOR = "hardcoreFor"

    /** The phone was just unlocked. */
    fun onUnlock(context: Context, now: Instant = Instant.now()) {
        val app = context.applicationContext as CompanionApp
        val inSession = runningSessionStart(app, now) != null && app.essentials.onUnlockInSession
        if (app.emergency.isActive(now) || inSession) {
            Log.d(TAG, "unlock: essentials (emergency ${app.emergency.isActive(now)}, session $inSession)")
            Takeover.startFromBackground(context, EssentialsActivity.intent(context))
        }
    }

    /** A session started on another device: asks LockMyPhone-Extended to start the hardcore profile, once per session. */
    fun maybeStartHardcore(context: Context, now: Instant = Instant.now()) {
        val app = context.applicationContext as CompanionApp
        if (!app.essentials.useLockMyPhone) return
        val profile = app.essentials.hardcoreProfile ?: return
        val (start, endsAt) = runningSessionStart(app, now) ?: return
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        if (prefs.getString(KEY_HARDCORE_FOR, null) == start.toString()) {
            return
        }

        prefs.edit().putString(KEY_HARDCORE_FOR, start.toString()).apply()
        val intent = Intent(LMP_START).setPackage(LMP_PACKAGE).putExtra("profile", profile)
        endsAt?.let { end -> Duration.between(now, end).toMinutes().takeIf { it > 0 }?.let { intent.putExtra("duration_min", it.toInt()) } }
        runCatching { context.sendBroadcast(intent) }.onFailure { Log.w(TAG, "LockMyPhone-Extended not reached", it) }
        Log.i(TAG, "hardcore: asked LockMyPhone-Extended for '$profile' until $endsAt")
    }

    /** Starts a LockMyPhone-Extended lock with [profile] for [minutes] (Settings → Essentials → try it for 1 minute). */
    fun startTestLock(context: Context, profile: String, minutes: Int = 1) {
        val intent = Intent(LMP_START).setPackage(LMP_PACKAGE).putExtra("profile", profile).putExtra("duration_min", minutes)
        runCatching { context.sendBroadcast(intent) }.onFailure { Log.w(TAG, "LockMyPhone-Extended not reached", it) }
        Log.i(TAG, "test: asked LockMyPhone-Extended for '$profile' for $minutes min")
    }

    /** True when LockMyPhone-Extended is installed. */
    fun lockMyPhoneInstalled(context: Context): Boolean =
        runCatching { context.packageManager.getPackageInfo(LMP_PACKAGE, 0) }.isSuccess

    // The newest PC file's running focus session (start, end), not a break, its phase not over; else the linked PC's.
    internal fun runningSessionStart(app: CompanionApp, now: Instant): Pair<Instant, Instant?>? {
        val fromFile = app.sync.received.value
            .filter { it.file.device.kind == SyncDeviceCard.KIND_PC && Duration.between(it.file.device.writtenAt, now) <= Duration.ofHours(12) }
            .mapNotNull { it.file.running }
            .filter { it.phaseEndsAt?.let { end -> now.isBefore(end) } ?: true }
            .maxByOrNull { it.phaseStartedAt }
            ?.let { it.phaseStartedAt to it.phaseEndsAt }
        if (fromFile != null) {
            return fromFile
        }

        val state = app.link.lastState.value?.state ?: return null
        if (!state.isActive || state.isScheduledBlock) return null
        val started = state.phaseStartedAt?.toInstant() ?: return null
        return started to state.phaseEndsAt?.toInstant()
    }
}
