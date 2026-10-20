package com.accountabilibuddy.companion.model

import java.util.UUID

/** Focus time the schedule plans: one occurrence of a block, with time off already taken out. */
data class PlannedFocus(val profileId: UUID, val window: LocalWindow)
