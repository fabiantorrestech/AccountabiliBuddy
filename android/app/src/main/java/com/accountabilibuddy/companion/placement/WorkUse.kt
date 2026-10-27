package com.accountabilibuddy.companion.placement

import java.time.Instant

/**
 * One "Need it for work" use. Stays on the phone: the sync file carries only the end time of the one under way
 * (like every reason on this phone, [note] never leaves it).
 *
 * @property sessionStart The PC session it belongs to, as the phone saw it ([PlacementSession]).
 */
data class WorkUse(
    val sessionStart: Instant,
    val start: Instant,
    val end: Instant,
    val kind: WorkUseKind,
    val note: String,
)
