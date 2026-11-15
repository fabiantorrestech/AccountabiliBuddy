package com.accountabilibuddy.shared.session

/** The places a running session locks on the phone (R11). Each lock consumer of the app maps to one of these. */
enum class PhoneLockArea {
    /** The day plan (the PC's Plan page is locked the same way). */
    PLAN,

    /** Sync settings, group membership, leaving a group. */
    SYNC,

    /** Device removal. */
    REMOVAL,

    /** Restore from a backup. */
    RESTORE,

    /** Setup revokes. */
    SETUP_REVOKES,

    /** Every other Settings page (sleep switches, calendar rules, goals, notifications, and so on). */
    OTHER_SETTINGS,

    /** The Guards page's own rows other than the enforcement rows (placement, emergency time, work-use rules). */
    GUARDS_OTHER,

    /** Guards' LockMyPhone and Essentials rows: the enforcement a session profile turns on. */
    GUARDS_LMP_ESSENTIALS,

    /** Curbox rows (blocks and hard mode). */
    CURBOX_ROWS,

    /** The phone session profiles. */
    SESSION_PROFILES,

    /** "Ending a phone session early" (the wait and reason setting). */
    END_EARLY_SETTING,
}

/**
 * R11 by mode. AccountabiliBuddy keeps the full no-escape-hatch locks; a ProductaBuddy phone session locks only the enforcement its session
 * profile turns on (the LMP/Essentials rows, the Curbox rows) and the two session settings (owner answer, ADR 85 over ADR 68 for phone sessions). Looser changes never apply during a phone
 * session in either mode (the ADR 68 "now" path excepted): that is a rule about changes, not an area, and lives with the settings code.
 */
object PhoneSessionLocks {
    // What a ProductaBuddy phone session always locks: the two session settings (R11). Its enforcement rows follow the profile.
    private val PRODUCTA_BUDDY_ALWAYS: Set<PhoneLockArea> = setOf(PhoneLockArea.SESSION_PROFILES, PhoneLockArea.END_EARLY_SETTING)

    /** What a running phone session locks in [mode], for a profile that turns on [profile]'s enforcement (ProductaBuddy: only that). */
    fun scope(mode: PhoneSessionMode, profile: PhoneSessionProfileUse): Set<PhoneLockArea> = when (mode) {
        PhoneSessionMode.ACCOUNTABILI_BUDDY -> PhoneLockArea.values().toSet()
        PhoneSessionMode.PRODUCTA_BUDDY -> buildSet {
            addAll(PRODUCTA_BUDDY_ALWAYS)
            if (profile.lockMyPhoneOrEssentials) add(PhoneLockArea.GUARDS_LMP_ESSENTIALS)
            if (profile.curbox) add(PhoneLockArea.CURBOX_ROWS)
        }
    }

    /** What a phone session locks in [mode], or nothing when none runs (a test session keeps its existing path). */
    fun scope(mode: PhoneSessionMode, record: PhoneSessionRecord?, profile: PhoneSessionProfileUse): Set<PhoneLockArea> =
        if (record != null && record.isRunning && !record.isTest) scope(mode, profile) else emptySet()

    /** When a PC session and the phone's own session both run (an offline double start) the lock is the stricter of the two. */
    fun stricter(a: Set<PhoneLockArea>, b: Set<PhoneLockArea>): Set<PhoneLockArea> = a + b
}

/**
 * What a phone session profile turns on, for the ProductaBuddy lock scope (owner: a PB phone session locks only what its profile turns on):
 * a LockMyPhone profile (or the Essentials screen) and/or Curbox (a focus group or blocks). A profile that is a plain timer turns on nothing.
 */
data class PhoneSessionProfileUse(val lockMyPhoneOrEssentials: Boolean, val curbox: Boolean) {
    companion object {
        val NOTHING = PhoneSessionProfileUse(lockMyPhoneOrEssentials = false, curbox = false)
    }
}
