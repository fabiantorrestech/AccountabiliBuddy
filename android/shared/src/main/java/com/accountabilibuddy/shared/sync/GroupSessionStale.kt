package com.accountabilibuddy.shared.sync

import java.time.Instant
import java.util.UUID

/** A phone session that has gone quiet (R3): Start comes back with the muted line "<name>'s session last heard from 2:05 PM". */
data class GroupSessionStale(val deviceId: UUID, val name: String, val lastHeardAt: Instant)
