package com.accountabilibuddy.shared.sync

import java.time.Instant
import java.util.UUID

/**
 * The session another device of the group has running (ADR 85, R2): who, and what the "Running on <name> · 32 min left" text needs.
 * The twin of Core's `GroupSessionLive`.
 *
 * @property endsAt When it ends as it stands now (the end moved on by the time away while it is paused); null for no set end.
 * @property paused The device is away with its timer paused (Pause timer, lunch, device off): "· paused".
 * @property noSetEnd The session has no set end: "· no set end".
 */
data class GroupSessionLive(
    val deviceId: UUID,
    val name: String,
    val kind: String,
    val endsAt: Instant?,
    val paused: Boolean,
    val noSetEnd: Boolean,
)
