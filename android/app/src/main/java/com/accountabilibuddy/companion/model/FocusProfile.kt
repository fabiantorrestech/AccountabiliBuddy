package com.accountabilibuddy.companion.model

import java.time.Duration
import java.util.UUID

/** What "on track" means for a session, e.g. a "Coding" profile (imported from the Windows app). */
data class FocusProfile(
    val id: UUID,
    val name: String,
    val rules: List<Rule>,
    val unknownGracePeriod: Duration,
    val unlockOptions: List<UnlockOption> = emptyList(),
    val askAboutUnlistedSites: Boolean = true,
)
