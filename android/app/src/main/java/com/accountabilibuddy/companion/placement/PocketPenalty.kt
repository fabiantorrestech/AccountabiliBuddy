package com.accountabilibuddy.companion.placement

/**
 * The two pocket penalties of the "Phone down" screen. Each has its own switch next to the placement switch, on by default.
 *
 * @property keyOn Preference key of the switch.
 * @property keyOffAt Preference key of the time a pending off is due.
 */
enum class PocketPenalty(val keyOn: String, val keyOffAt: String) {
    /** While the placement reads pocket, the screen's options appear after a wait. */
    UnlockFriction("unlockFriction", "unlockFrictionOffAt"),

    /** A session with more than about 5 minutes in a pocket isn't phone-free. */
    NoPhoneFreeCredit("noPhoneFreeCredit", "noPhoneFreeCreditOffAt"),
}
