package com.accountabilibuddy.shared.session

/**
 * What the phone's essentials hook decides about the phone's OWN session (PS6a; ADR 85, owner answers 2026-11-15): pure, no Android types, no clock.
 *
 * - The order of the sources: the phone's own session, then a PC session, then the test session ([runningStart]).
 * - ProductaBuddy: a phone session locks only what its session profile turns on, so the global Essentials screen, the global LockMyPhone
 *   profile and the global Curbox group are NOT turned on by it; AccountabiliBuddy falls back to the global ones (B7b of the plan check).
 * - A session on another device (a PC's, or the test session) running at the same time keeps the global locks in both modes: a phone timer never
 *   cancels them, the stricter of the two wins (plan check B9; PS6a review B1).
 * - A break on a PC never loosens the phone's own session ([onBreak], B7c).
 */
object OwnSessionEnforcement {
    /**
     * What a session profile turns on for the essentials hook. Phone session profiles are read by PS2c (the editor is PS3b); [NONE] stands for a plain timer (no profile found): AccountabiliBuddy still falls back to the global settings, ProductaBuddy locks nothing.
     */
    data class Profile(val lmpProfile: String?, val essentialsOnUnlock: Boolean, val curboxFocusGroup: String?, val curboxBlocks: Boolean = false) {
        /** The same two flags [PhoneSessionLocks] reads: LockMyPhone or Essentials, and Curbox (a focus group or blocks). */
        val use: PhoneSessionProfileUse get() = PhoneSessionProfileUse(lmpProfile != null || essentialsOnUnlock, curboxFocusGroup != null || curboxBlocks)

        companion object {
            val NONE = Profile(lmpProfile = null, essentialsOnUnlock = false, curboxFocusGroup = null)
        }
    }

    /** The running session the lock code follows: own first, then a PC's, then the test session (offline double start: the phone's own wins). */
    fun <T : Any> runningStart(own: T?, pc: T?, test: T?): T? = own ?: pc ?: test

    /** True when the PC's running session is on a break: never while the phone's own session runs (the PC's break must not loosen it). */
    fun onBreak(ownRunning: Boolean, pcOnBreak: Boolean): Boolean = !ownRunning && pcOnBreak

    /** The LockMyPhone profile an own session asks for: the profile's own, else the global hardcore profile (AccountabiliBuddy, or another device's session runs too). */
    fun lmpProfile(mode: PhoneSessionMode, profile: Profile, globalHardcore: String?, otherRunning: Boolean): String? =
        profile.lmpProfile ?: globalHardcore.takeIf { globalApplies(mode, otherRunning) }

    /** Whether the Essentials screen (or LockMyPhone's lock in its place) comes up on unlock in an own session. */
    fun essentialsOnUnlock(mode: PhoneSessionMode, profile: Profile, globalOnUnlock: Boolean, otherRunning: Boolean): Boolean =
        profile.essentialsOnUnlock || (globalApplies(mode, otherRunning) && globalOnUnlock)

    /** The Curbox focus group an own session starts: the profile's, else the global hard-mode group (given as null when hard mode is off). */
    fun curboxGroup(mode: PhoneSessionMode, profile: Profile, globalGroup: String?, otherRunning: Boolean): String? =
        profile.curboxFocusGroup ?: globalGroup.takeIf { globalApplies(mode, otherRunning) }

    private fun globalApplies(mode: PhoneSessionMode, otherRunning: Boolean): Boolean = mode == PhoneSessionMode.ACCOUNTABILI_BUDDY || otherRunning
}
