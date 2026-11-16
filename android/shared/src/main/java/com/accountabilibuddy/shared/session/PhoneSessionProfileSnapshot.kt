package com.accountabilibuddy.shared.session

/**
 * What a phone session profile turned on, copied into the session's record when it STARTS (PS2c review S3, PS6a review N6): every reader of a running
 * session's enforcement (the essentials hook, Curbox, the usage summary) reads this copy and never looks up the live profile store, so no later writer of the
 * profiles (the editor's pending-edit settle, a restore, an import) can change a running session's locks.
 *
 * Phone-local: it sits in the sealed session store only and never in the sync file (R14); [toString] leaves the app list out, so no log line can carry it.
 */
data class PhoneSessionProfileSnapshot(
    val lmpProfile: String? = null,
    val curboxFocusGroup: String? = null,
    val curboxBlocks: List<CurboxBlockRef> = emptyList(),
    val distractingApps: List<String> = emptyList(),
) {
    /** The enforcement the essentials hook reads (a LockMyPhone profile is the lock; the Essentials screen has no switch of its own on a profile). */
    val enforcement: OwnSessionEnforcement.Profile
        get() = OwnSessionEnforcement.Profile(lmpProfile, essentialsOnUnlock = false, curboxFocusGroup = curboxFocusGroup, curboxBlocks = curboxBlocks.isNotEmpty())

    override fun toString(): String =
        "PhoneSessionProfileSnapshot(lmp=${lmpProfile != null}, curboxGroup=${curboxFocusGroup != null}, blocks=${curboxBlocks.size}, apps=${distractingApps.size})"

    companion object {
        /** A plain timer: the built-in profile, or a session started before profiles were copied (it turns nothing on). */
        val NONE = PhoneSessionProfileSnapshot()
    }
}
