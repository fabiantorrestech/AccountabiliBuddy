package com.accountabilibuddy.companion.link

/** Where the link to the PC stands. */
sealed interface LinkStatus {
    /** Not paired with a PC. */
    data object Unlinked : LinkStatus

    /** Paired, not connected (the app isn't on screen). */
    data object Idle : LinkStatus

    data object Connecting : LinkStatus

    data class Connected(val pcName: String, val state: PcState?) : LinkStatus

    /** Paired, but the PC can't be reached right now. */
    data class Offline(val message: String) : LinkStatus
}
