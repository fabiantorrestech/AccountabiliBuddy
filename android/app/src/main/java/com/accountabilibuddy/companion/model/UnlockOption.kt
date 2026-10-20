package com.accountabilibuddy.companion.model

import java.time.Duration
import java.util.UUID

/** A way to lift a profile's blocks, e.g. "Off day": wait [wait], then the blocks pause until relock. */
data class UnlockOption(val id: UUID, val name: String, val wait: Duration)
