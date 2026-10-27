package com.accountabilibuddy.companion.placement

import com.accountabilibuddy.shared.sync.SyncPlacement

/** Where the phone is during a PC focus session. [wire] is the text the sync file carries ([SyncPlacement]). */
enum class PlacementState(val wire: String) {
    Bag(SyncPlacement.BAG),
    Desk(SyncPlacement.DESK),
    FaceUp(SyncPlacement.FACE_UP),
    Pocket(SyncPlacement.POCKET),
    InUse(SyncPlacement.IN_USE),
    Unknown(SyncPlacement.UNKNOWN);

    /** True for the two states that count as "the phone is out" (when there is no work use). */
    val isOut: Boolean get() = this == InUse || this == FaceUp

    companion object {
        fun fromWire(text: String?): PlacementState = entries.firstOrNull { it.wire == text } ?: Unknown
    }
}
