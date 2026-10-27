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
 * When the essentials screen comes up by itself, and the LockMyPhone-Extended "hardcore" profiles.
 * - Unlocking during emergency phone time, or during a session on another device (Settings switch, on by default),
 *   lands on the essentials screen; unlocking during the night (bedtime until wake, Settings → Sleep, off by default)
 *   lands on the bedtime essentials screen. A session on another device wins over the night.
 * - Hardcore: when a session starts on another device, LockMyPhone-Extended's public intent API starts the named
 *   profile for the session's length; at bedtime, the night's profile until the wake time (that app enforces; this
 *   one only asks it).
 * - The phone's test session (Today → More options) counts as a session on another device here, unless a real one runs.
 */
object EssentialsLaunch {
    private const val TAG = "Essentials"
    /** LockMyPhone-Extended's package. */
    const val LOCK_MY_PHONE = "com.fabiantorrestech.lockmyphone_extended"
    private const val LMP_PACKAGE = LOCK_MY_PHONE
    private const val LMP_START = "$LMP_PACKAGE.action.START_LOCK"
    private const val PREFS = "essentials-launch"
    private const val KEY_HARDCORE_FOR = "hardcoreFor"
    private const val KEY_BEDTIME_FOR = "bedtimeFor"
    /** The first LockMyPhone-Extended that keeps more than one synced profile (its ADR 0010, 0.13.0). */
    private const val SLOTS_SINCE_VERSION_CODE = 7L

    /** The phone was just unlocked. */
    fun onUnlock(context: Context, now: Instant = Instant.now()) {
        val app = context.applicationContext as CompanionApp
        val session = runningSessionStart(app, now) != null
        val night = if (session) null else BedtimeNight.current(app)
        if (night != null) maybeStartBedtimeHardcore(context, now)
        val kind = when {
            // A work use ("Need it for work") lets the phone be unlocked and used for its minutes.
            session && app.essentials.onUnlockInSession && app.workUse.active(now) == null -> EssentialsKind.Focus
            night != null && app.bedtimeEssentials.onUnlockInSession -> EssentialsKind.Bedtime
            app.emergency.isActive(now) -> EssentialsKind.Focus
            else -> null
        }
        Log.d(TAG, "unlock: ${kind ?: "no"} essentials (emergency ${app.emergency.isActive(now)}, session $session, night $night)")
        // In a session with LockMyPhone-Extended set up, that app's lock is the screen, not this one.
        if (kind == EssentialsKind.Focus && session && !app.emergency.isActive(now) && startLockMyPhoneForSession(context, now)) return
        kind?.let { Takeover.startFromBackground(context, EssentialsActivity.intent(context, it)) }
    }

    /**
     * During the night (and no session on another device): asks LockMyPhone-Extended to start the night's profile
     * until the wake time, once per night.
     */
    fun maybeStartBedtimeHardcore(context: Context, now: Instant = Instant.now()) {
        val app = context.applicationContext as CompanionApp
        if (!app.essentials.useLockMyPhone) return
        val profile = app.bedtimeEssentials.hardcoreProfile ?: return
        if (runningSessionStart(app, now) != null) return
        val night = BedtimeNight.current(app) ?: return
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        if (prefs.getString(KEY_BEDTIME_FOR, null) == night.start.toString()) {
            return
        }

        prefs.edit().putString(KEY_BEDTIME_FOR, night.start.toString()).apply()
        val wake = night.end.atZone(java.time.ZoneId.systemDefault()).toInstant()
        val minutes = Duration.between(now, wake).toMinutes()
        if (minutes <= 0) return
        val intent = Intent(LMP_START).setPackage(LMP_PACKAGE).putExtra("profile", profile).putExtra("duration_min", minutes.toInt())
        runCatching { context.sendBroadcast(intent) }.onFailure { Log.w(TAG, "LockMyPhone-Extended not reached", it) }
        Log.i(TAG, "bedtime: asked LockMyPhone-Extended for '$profile' until ${night.end}")
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
        // Rounded up, so a 1-minute test asked a moment after it started still gets its minute.
        endsAt?.let { end -> ceilMinutes(Duration.between(now, end)).takeIf { it > 0 }?.let { intent.putExtra("duration_min", it.toInt()) } }
        runCatching { context.sendBroadcast(intent) }.onFailure { Log.w(TAG, "LockMyPhone-Extended not reached", it) }
        Log.i(TAG, "hardcore: asked LockMyPhone-Extended for '$profile' until $endsAt")
    }

    /**
     * Unlocked during a session with "use LockMyPhone-Extended" on: asks it to start the hardcore profile for what is left
     * of the session (no once-per-session guard: it is asked again at each unlock). False when it can't be used (switch
     * off, no profile chosen, not installed), so the caller shows the in-app screen.
     */
    private fun startLockMyPhoneForSession(context: Context, now: Instant): Boolean {
        val app = context.applicationContext as CompanionApp
        if (!app.essentials.useLockMyPhone || !lockMyPhoneInstalled(context)) return false
        val profile = app.essentials.hardcoreProfile ?: return false
        val (_, endsAt) = runningSessionStart(app, now) ?: return false
        val intent = Intent(LMP_START).setPackage(LMP_PACKAGE).putExtra("profile", profile)
        endsAt?.let { end -> ceilMinutes(Duration.between(now, end)).takeIf { it > 0 }?.let { intent.putExtra("duration_min", it.toInt()) } }
        if (runCatching { context.sendBroadcast(intent) }.isFailure) return false
        Log.i(TAG, "unlock in a session: asked LockMyPhone-Extended for '$profile' until $endsAt")
        return true
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

    /** True when the installed LockMyPhone-Extended keeps a synced profile per slot (0.13.0 on). */
    fun lockMyPhoneHasSlots(context: Context): Boolean =
        runCatching { androidx.core.content.pm.PackageInfoCompat.getLongVersionCode(context.packageManager.getPackageInfo(LMP_PACKAGE, 0)) }
            .getOrNull()?.let { it >= SLOTS_SINCE_VERSION_CODE } ?: false

    /** Whole minutes in [left], rounded up. */
    internal fun ceilMinutes(left: Duration): Long = (left.seconds + 59) / 60

    // A session on another device (it always wins), else the phone's test session (Today → More options).
    internal fun runningSessionStart(app: CompanionApp, now: Instant): Pair<Instant, Instant?>? =
        realSessionStart(app, now) ?: app.testSession.running(now)

    // The newest PC file's running focus session (start, end), not a break, its phase not over.
    internal fun realSessionStart(app: CompanionApp, now: Instant): Pair<Instant, Instant?>? {
        return app.sync.received.value
            .filter { it.file.device.kind == SyncDeviceCard.KIND_PC && Duration.between(it.file.device.writtenAt, now) <= Duration.ofHours(12) }
            .mapNotNull { it.file.running }
            .filter { it.phaseEndsAt?.let { end -> now.isBefore(end) } ?: true }
            .maxByOrNull { it.phaseStartedAt }
            ?.let { it.phaseStartedAt to it.phaseEndsAt }
    }
}
