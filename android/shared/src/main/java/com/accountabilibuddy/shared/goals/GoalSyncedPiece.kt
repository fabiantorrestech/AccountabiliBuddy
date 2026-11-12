package com.accountabilibuddy.shared.goals

import java.time.Instant

/**
 * One synced session piece as goals read it: the sealed sync file's `sessions` item (the PC's `SyncSession`) with the
 * checklist's label fields only. A missing `tasks` ([tasks] null) is not the same as an empty one: only a piece with no
 * checklist at all can continue the piece before it ([GoalFocusAttribution.fromSynced]).
 *
 * @property end Null for the session that was running when the file was written.
 * @property profile The profile's name; null for a session added by hand.
 * @property added Added by hand in History (ADR 20): counts toward goals, never toward a guard.
 * @property offMinutesBefore ADR 60: how long the PC was off just before this piece; null for none.
 */
data class GoalSyncedPiece(
    val start: Instant,
    val end: Instant?,
    val profile: String?,
    val added: Boolean,
    val tasks: List<GoalSessionTask>? = null,
    val offMinutesBefore: Int? = null,
)
