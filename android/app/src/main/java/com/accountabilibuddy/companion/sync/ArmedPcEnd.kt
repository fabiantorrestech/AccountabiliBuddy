package com.accountabilibuddy.companion.sync

import java.time.Instant
import java.util.UUID

/**
 * The phone's own armed end of a PC session (ADR 66): confirmed during the PC's wait, it is sent by itself at [sendAt] (the
 * offer's allowed-from time plus the clocks' skew) as the existing end-early action. Only the reason's length is kept; the reason
 * never leaves the phone and isn't stored.
 */
data class ArmedPcEnd(val pcId: UUID, val pcName: String, val requestId: String, val reasonLength: Int, val sendAt: Instant)
