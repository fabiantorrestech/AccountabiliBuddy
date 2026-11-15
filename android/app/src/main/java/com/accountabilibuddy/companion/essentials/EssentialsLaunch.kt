package com.accountabilibuddy.companion.essentials

import android.content.Context
import android.content.Intent
import android.util.Log
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.sync.PcFileControl
import com.accountabilibuddy.companion.nudge.Takeover
import com.accountabilibuddy.companion.placement.PhoneDownLaunch
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import java.time.Duration
import java.time.Instant

/**
 * When the essentials screen comes up by itself, and the LockMyPhone-Extended "hardcore" profiles. ADR 73: with
 * LockMyPhone-Extended as the lock, its lock replaces the screen everywhere ([showEssentials]; the hold is [SessionLock]).
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
    internal const val LMP_STOP = "$LMP_PACKAGE.action.STOP_LOCK"
    private const val PREFS = "essentials-launch"
    private const val KEY_HARDCORE_FOR = "hardcoreFor"
    private const val KEY_BEDTIME_FOR = "bedtimeFor"
    /** The session start (ISO) AccountabiliBuddy itself asked LockMyPhone-Extended to lock for; the only lock lunch may lift. */
    internal const val KEY_SESSION_LOCK_FOR = "sessionLockFor"
    private const val KEY_TEST_LOCK_UNTIL = "testLockUntil"
    /** The first LockMyPhone-Extended that keeps more than one synced profile (its ADR 0010, 0.13.0). */
    private const val SLOTS_SINCE_VERSION_CODE = 7L

    /** The phone was just unlocked. */
    fun onUnlock(context: Context, now: Instant = Instant.now()) {
        val app = context.applicationContext as CompanionApp
        // ADR 73: emergency phone time and "Need it for work" leave the phone free, in both modes (no screen, no lock of ours).
        if (app.emergency.isActive(now) || app.workUse.active(now) != null) {
            SessionLock.reconcile(context, now)
            Log.d(TAG, "unlock: free (emergency or work use)")
            return
        }
        // Lunch is a break from the session: no essentials screen (and no lock) until it ends.
        val session = runningSessionStart(app, now) != null && !app.lunch.isOnLunch(now)
        // A session on another device wins over the night. ADR 68/75: ProductaBuddy keeps no night unless it was chosen.
        val night = if (runningSessionStart(app, now) != null || !nightAllowed(app)) null else BedtimeNight.current(app)
        Log.d(TAG, "unlock: session $session, night $night")
        when {
            session && app.essentials.onUnlockInSession -> showEssentials(context, now, fromUnlock = true)
            // The night: LockMyPhone-Extended's lock or the bedtime screen, never both (ADR 73).
            night != null && app.bedtimeEssentials.onUnlockInSession ->
                if (app.lockScreen.usesLockMyPhone(now)) maybeStartBedtimeHardcore(context, now)
                else Takeover.startFromBackground(context, EssentialsActivity.intent(context, EssentialsKind.Bedtime))
        }
    }

    /** True when the night may engage: always in AccountabiliBuddy, in ProductaBuddy only when chosen (ADR 75). */
    fun nightAllowed(app: CompanionApp): Boolean = !app.guidance.isCompanion || app.bedtimeEssentials.nightInProductaBuddy

    /**
     * Wherever the focus essentials screen came up during a session (unlock, 3 minutes out of place, the work card and
     * Phone down's buttons): LockMyPhone-Extended's lock when it is the lock (ADR 73), else the built-in screen, or Phone
     * down on an unlock when the placement guard asks for it.
     */
    fun showEssentials(context: Context, now: Instant = Instant.now(), fromUnlock: Boolean = false) {
        if (lockWithLockMyPhone(context, now)) return
        if (fromUnlock && PhoneDownLaunch.shouldShowOnUnlock(context, now)) {
            PhoneDownLaunch.showOnUnlock(context, now)
            return
        }
        Takeover.startFromBackground(context, EssentialsActivity.intent(context, EssentialsKind.Focus))
    }

    /**
     * For a screen's Essentials button: null after LockMyPhone-Extended was asked to lock (the caller just closes), else
     * the built-in essentials screen to open.
     */
    fun essentialsIntentOrLock(context: Context, now: Instant = Instant.now()): Intent? =
        if (lockWithLockMyPhone(context, now)) null else EssentialsActivity.intent(context)

    /** The label of a screen's Essentials button: says whether it opens this app's screen or locks with LockMyPhone-Extended. */
    fun essentialsButtonLabel(context: Context): String =
        if ((context.applicationContext as CompanionApp).lockScreen.usesLockMyPhone()) "Lock (LockMyPhone-Extended)" else "Essentials"

    /** Asks LockMyPhone-Extended to lock for the session when it is the lock; false when the built-in screen must stand in. */
    private fun lockWithLockMyPhone(context: Context, now: Instant): Boolean {
        val app = context.applicationContext as CompanionApp
        if (!SessionLock.lockMyPhoneLocks(context, now)) return false
        // No session (the work card's "Essentials only" in work time): a plain lock with the focus profile, its own length.
        if (runningSessionStart(app, now) == null) {
            return sendSessionLock(context, app.essentials.hardcoreProfile, endsAt = null, now = now)
        }
        if (SessionLock.holdCapable(context)) {
            SessionLock.reconcile(context, now)
            // Refused just now (no profile, no device admin there): the built-in screen stands in.
            return SessionLock.lockMyPhoneLocks(context, now)
        }
        // An LockMyPhone-Extended without the hold: the older START_LOCK path.
        return startLockMyPhoneForSession(context, now)
    }

    /**
     * During the night (and no session on another device): asks LockMyPhone-Extended to start the night's profile
     * until the wake time, once per night.
     */
    fun maybeStartBedtimeHardcore(context: Context, now: Instant = Instant.now()) {
        val app = context.applicationContext as CompanionApp
        if (!app.essentials.useLockMyPhone || !app.bedtimeEssentials.onUnlockInSession) return
        // ADR 68/75: in ProductaBuddy only when the night was chosen there.
        if (!nightAllowed(app)) return
        val profile = app.bedtimeEssentials.hardcoreProfile
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
        if (!app.essentials.useLockMyPhone || !app.essentials.onUnlockInSession) return
        val profile = app.essentials.hardcoreProfile
        val (start, endsAt) = runningSessionStart(app, now) ?: return
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        if (prefs.getString(KEY_HARDCORE_FOR, null) == start.toString()) {
            return
        }

        // Lunch has lifted the lock: LunchLockLift asks for it again when lunch ends.
        if (LunchLockLift.liftingNow(context, now)) return
        prefs.edit().putString(KEY_HARDCORE_FOR, start.toString()).putString(KEY_SESSION_LOCK_FOR, start.toString()).apply()
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
        val profile = app.essentials.hardcoreProfile
        val (start, endsAt) = runningSessionStart(app, now) ?: return false
        // During a lunch the lock is lifted on purpose: unlocking does not bring it back (nor the in-app screen).
        if (LunchLockLift.liftingNow(context, now)) return true
        if (!sendSessionLock(context, profile, endsAt, now)) return false
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putString(KEY_SESSION_LOCK_FOR, start.toString()).apply()
        Log.i(TAG, "unlock in a session: asked LockMyPhone-Extended for '$profile' until $endsAt")
        return true
    }

    /** Asks LockMyPhone-Extended for [profile] until [endsAt] (no set end: its own length). False when the broadcast failed. */
    internal fun sendSessionLock(context: Context, profile: String, endsAt: Instant?, now: Instant): Boolean {
        val intent = Intent(LMP_START).setPackage(LMP_PACKAGE).putExtra("profile", profile)
        endsAt?.let { end -> ceilMinutes(Duration.between(now, end)).takeIf { it > 0 }?.let { intent.putExtra("duration_min", it.toInt()) } }
        return runCatching { context.sendBroadcast(intent) }.isSuccess
    }

    /** True while a test lock started here may still be running (Settings → Essentials → "Test stop"). */
    fun testLockMayRun(context: Context, now: Instant = Instant.now()): Boolean =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getLong(KEY_TEST_LOCK_UNTIL, 0L) > now.toEpochMilli()

    /** Starts a LockMyPhone-Extended lock with [profile] for [minutes] (Settings → Essentials → try it for 1 minute). */
    fun startTestLock(context: Context, profile: String, minutes: Int = 1) {
        val intent = Intent(LMP_START).setPackage(LMP_PACKAGE).putExtra("profile", profile).putExtra("duration_min", minutes)
        runCatching { context.sendBroadcast(intent) }.onFailure { Log.w(TAG, "LockMyPhone-Extended not reached", it) }
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
            .putLong(KEY_TEST_LOCK_UNTIL, Instant.now().plus(Duration.ofMinutes(minutes.toLong())).toEpochMilli()).apply()
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

    /** True when the PC's running session is on a break right now (the lock isn't re-asked for then). */
    internal fun sessionOnBreak(app: CompanionApp, now: Instant): Boolean =
        app.sync.received.value
            .filter { it.file.device.kind == SyncDeviceCard.KIND_PC }
            .filter { PcFileControl.guardsHold(it.file, now) }
            .mapNotNull { PcFileControl.liveRunning(it.file, now) }
            .filter { it.phaseEndAt(now)?.let { end -> now.isBefore(end) } ?: true }
            .maxByOrNull { it.phaseStartedAt }?.isBreak == true

    // The newest PC file's running focus session (start, end), its phase not over. Counted by the same rule as the PC card
    // and the header ([PcFileControl.liveRunning]): a session with no set end only while its file is under 30 minutes old,
    // so a PC file that stopped arriving (a stalled sync) never holds the phone in the essentials screen.
    internal fun realSessionStart(app: CompanionApp, now: Instant): Pair<Instant, Instant?>? {
        return app.sync.received.value
            .filter { it.file.device.kind == SyncDeviceCard.KIND_PC }
            // ADR 60: a PC that went off holds the phone for 10 minutes, then lets go until it's back.
            .filter { PcFileControl.guardsHold(it.file, now) }
            .mapNotNull { PcFileControl.liveRunning(it.file, now) }
            .filter { it.phaseEndAt(now)?.let { end -> now.isBefore(end) } ?: true }
            .maxByOrNull { it.phaseStartedAt }
            ?.let { it.phaseStartedAt to it.phaseEndAt(now) }
    }
}
