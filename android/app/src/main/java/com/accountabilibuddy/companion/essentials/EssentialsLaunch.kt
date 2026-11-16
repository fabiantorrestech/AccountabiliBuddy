package com.accountabilibuddy.companion.essentials

import android.content.Context
import android.content.Intent
import android.util.Log
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.sync.PcFileControl
import com.accountabilibuddy.companion.nudge.Takeover
import com.accountabilibuddy.companion.placement.PhoneDownLaunch
import com.accountabilibuddy.shared.session.OwnSessionEnforcement
import com.accountabilibuddy.shared.session.PhoneSessionMode
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.companion.guidance.TrustedTime
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
    fun onUnlock(context: Context, now: Instant = TrustedTime.now()) {
        val app = context.applicationContext as CompanionApp
        // ADR 73: emergency phone time and "Need it for work" leave the phone free, in both modes (no screen, no lock of ours).
        if (app.emergency.isActive(now) || app.workUse.active(now) != null) {
            SessionLock.reconcile(context, now)
            Log.d(TAG, "unlock: free (emergency or work use)")
            return
        }
        // Lunch is a break from the session: no essentials screen (and no lock) until it ends.
        // Owner design (2026-11-15): a session on this phone takes over the night, in either mode, as one on another device does; the night
        // comes back 5 minutes after it ends ([NightHandover]: a reminder at night start, then a card with Snooze and Cancel).
        NightHandover.check(context, now)
        val session = runningSessionStart(app, now) != null && !app.lunch.isOnLunch(now)
        // A session on another device wins over the night. ADR 68/75: ProductaBuddy keeps no night unless it was chosen.
        val night = if (nightSessionRunning(app, now) || !nightAllowed(app) || NightHandover.holds(app, now)) null else BedtimeNight.current(app, NightHandover.local(now))
        Log.d(TAG, "unlock: session $session, night $night")
        when {
            session && essentialsOnUnlock(app, now) -> showEssentials(context, now, fromUnlock = true)
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
    fun showEssentials(context: Context, now: Instant = TrustedTime.now(), fromUnlock: Boolean = false) {
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
    fun essentialsIntentOrLock(context: Context, now: Instant = TrustedTime.now()): Intent? =
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
        if (SessionLock.holdCapable(context) || SessionLock.holding(context)) {
            // An unlock re-sends the hold (idempotent): a lock that died on LockMyPhone-Extended's side comes back (S2).
            SessionLock.reconcile(context, now, resend = true)
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
    fun maybeStartBedtimeHardcore(context: Context, now: Instant = TrustedTime.now()) {
        val app = context.applicationContext as CompanionApp
        if (!app.essentials.useLockMyPhone || !app.bedtimeEssentials.onUnlockInSession) return
        // ADR 68/75: in ProductaBuddy only when the night was chosen there.
        if (!nightAllowed(app)) return
        val profile = app.bedtimeEssentials.hardcoreProfile
        if (nightSessionRunning(app, now) || NightHandover.holds(app, now)) return
        val night = BedtimeNight.current(app, NightHandover.local(now)) ?: return
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        if (prefs.getString(KEY_BEDTIME_FOR, null) == night.start.toString()) {
            return
        }

        prefs.edit().putString(KEY_BEDTIME_FOR, night.start.toString()).apply()
        val wake = night.end.atZone(java.time.ZoneId.systemDefault()).toInstant()
        val minutes = Duration.between(now, wake).toMinutes()
        if (minutes <= 0) return
        val intent = Intent(LMP_START).setPackage(LMP_PACKAGE).putExtra("profile", profile).putExtra("duration_min", minutes.toInt())
        LockMyPhoneLink.send(context, intent)
        Log.i(TAG, "bedtime: asked LockMyPhone-Extended for '$profile' until ${night.end}")
    }

    /** The night's lock is asked for again (the once-per-night guard is cleared): the night starts after a session handed it over ([NightHandover]). */
    internal fun forgetNightLock(context: Context) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().remove(KEY_BEDTIME_FOR).apply()
    }

    /** A session started on another device: asks LockMyPhone-Extended to start the hardcore profile, once per session. */
    fun maybeStartHardcore(context: Context, now: Instant = TrustedTime.now()) {
        val app = context.applicationContext as CompanionApp
        if (!app.essentials.useLockMyPhone || !essentialsOnUnlock(app, now)) return
        val (start, endsAt) = runningSessionStart(app, now) ?: return
        val profile = lmpProfile(app, now) ?: return
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
        LockMyPhoneLink.send(context, intent)
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
        val (start, endsAt) = runningSessionStart(app, now) ?: return false
        val profile = lmpProfile(app, now) ?: return false
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
        return LockMyPhoneLink.send(context, intent)
    }

    /** True while a test lock started here may still be running (Settings → Essentials → "Test stop"). */
    fun testLockMayRun(context: Context, now: Instant = TrustedTime.now()): Boolean =
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).getLong(KEY_TEST_LOCK_UNTIL, 0L) > now.toEpochMilli()

    /** Starts a LockMyPhone-Extended lock with [profile] for [minutes] (Settings → Essentials → try it for 1 minute). */
    fun startTestLock(context: Context, profile: String, minutes: Int = 1) {
        val intent = Intent(LMP_START).setPackage(LMP_PACKAGE).putExtra("profile", profile).putExtra("duration_min", minutes)
        LockMyPhoneLink.send(context, intent)
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
            .putLong(KEY_TEST_LOCK_UNTIL, TrustedTime.now().plus(Duration.ofMinutes(minutes.toLong())).toEpochMilli()).apply()
        Log.i(TAG, "test: asked LockMyPhone-Extended for '$profile' for $minutes min")
    }

    /** True when LockMyPhone-Extended is installed and is the real one: signed with this app's key ([LockMyPhoneTrust]). */
    fun lockMyPhoneInstalled(context: Context): Boolean = LockMyPhoneTrust.verified(context)

    /** True when the installed LockMyPhone-Extended keeps a synced profile per slot (0.13.0 on). */
    fun lockMyPhoneHasSlots(context: Context): Boolean =
        runCatching { androidx.core.content.pm.PackageInfoCompat.getLongVersionCode(context.packageManager.getPackageInfo(LMP_PACKAGE, 0)) }
            .getOrNull()?.let { it >= SLOTS_SINCE_VERSION_CODE } ?: false

    /** Whole minutes in [left], rounded up. */
    internal fun ceilMinutes(left: Duration): Long = (left.seconds + 59) / 60

    // PS6a (ADR 85): the phone's own session first, then a session on another device, else the phone's test session (Today → More options).
    // Offline double start: the phone's own wins. [realSessionStart] stays PC-only (Phone down and the end cost use it).
    internal fun runningSessionStart(app: CompanionApp, now: Instant): Pair<Instant, Instant?>? =
        OwnSessionEnforcement.runningStart(ownSessionStart(app, now), realSessionStart(app, now), app.testSession.running(now))

    /**
     * The phone's own running session (start, end), or null. Read from the store's flag, never compared with a clock (B1): [now] only
     * moves a paused timer's end. A test record is the test session's, not this one.
     */
    internal fun ownSessionStart(app: CompanionApp, now: Instant): Pair<Instant, Instant?>? =
        app.phoneSession.running.value?.takeIf { it.isRunning && !it.isTest }?.let { it.phaseStartedAt to it.effectiveEnd(now) }

    /**
     * What the own session's profile turns on: the profile the session started with (the record's `profileId`, PS6a review N6), read from the phone session
     * profiles (PS2c). No profile found, or no own session: nothing (a plain timer).
     */
    internal fun ownProfile(app: CompanionApp): OwnSessionEnforcement.Profile =
        app.phoneSession.running.value?.takeIf { it.isRunning && !it.isTest }?.let(app.phoneSessionProfiles::enforcementOf) ?: OwnSessionEnforcement.Profile.NONE

    internal fun mode(app: CompanionApp): PhoneSessionMode =
        if (app.guidance.isCompanion) PhoneSessionMode.PRODUCTA_BUDDY else PhoneSessionMode.ACCOUNTABILI_BUDDY

    /**
     * True while a REAL session (the phone's own, or one on a PC) runs: the only sessions that hold the night and hand it over. The test session
     * never delays the night (owner, 2026-11-15).
     */
    internal fun nightSessionRunning(app: CompanionApp, now: Instant): Boolean = ownRunning(app, now) || realSessionStart(app, now) != null

    /** True while the phone's own session runs (the PC's break never loosens it). */
    internal fun ownRunning(app: CompanionApp, now: Instant): Boolean = ownSessionStart(app, now) != null

    /**
     * Whether the Essentials screen (or the lock in its place) comes up in the running session. A session on another device keeps
     * the global switch in both modes (ADR 68); the phone's own session in ProductaBuddy only when its profile turns it on (B7b).
     */
    internal fun essentialsOnUnlock(app: CompanionApp, now: Instant = TrustedTime.now()): Boolean =
        if (ownRunning(app, now)) OwnSessionEnforcement.essentialsOnUnlock(mode(app), ownProfile(app), app.essentials.onUnlockInSession, otherSessionRunning(app, now))
        else app.essentials.onUnlockInSession

    /** The LockMyPhone profile the running session asks for; null when none (ProductaBuddy's own session without a profile one). */
    internal fun lmpProfile(app: CompanionApp, now: Instant = TrustedTime.now()): String? =
        if (ownRunning(app, now)) OwnSessionEnforcement.lmpProfile(mode(app), ownProfile(app), app.essentials.hardcoreProfile, otherSessionRunning(app, now))
        else app.essentials.hardcoreProfile

    /** The Curbox focus group the running session starts ([globalGroup]: the global hard-mode group, null when hard mode is off); null: none. */
    internal fun curboxGroup(app: CompanionApp, globalGroup: String?, now: Instant = TrustedTime.now()): String? =
        if (ownRunning(app, now)) OwnSessionEnforcement.curboxGroup(mode(app), ownProfile(app), globalGroup, otherSessionRunning(app, now)) else globalGroup

    /** True while a session on another device (a PC's) or the phone's test session runs: the global locks then stay whatever the own session says (review B1). */
    internal fun otherSessionRunning(app: CompanionApp, now: Instant): Boolean = realSessionStart(app, now) != null || app.testSession.running(now) != null

    /** True when the PC's running session is on a break right now (the lock isn't re-asked for then); never while the phone's own session runs (B7c). */
    internal fun sessionOnBreak(app: CompanionApp, now: Instant): Boolean =
        OwnSessionEnforcement.onBreak(ownRunning(app, now), pcOnBreak(app, now))

    private fun pcOnBreak(app: CompanionApp, now: Instant): Boolean =
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
