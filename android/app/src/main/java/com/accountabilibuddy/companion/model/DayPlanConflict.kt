package com.accountabilibuddy.companion.model

import java.time.Instant

/**
 * The day plan changed on the PC and on this phone since they last matched (ADR 19): both versions, when each was
 * edited and what each changed from the shared starting point, for the user to pick one.
 */
data class DayPlanConflict(
    val pcPlan: DayPlan,
    val pcVersion: String,
    val pcEditedAt: Instant?,
    val phonePlan: DayPlan,
    val phoneEditedAt: Instant,
    val pcChanges: List<String>,
    val phoneChanges: List<String>,
) {
    /** True when the PC's version was edited later (it gets the highlight); unknown PC times count as older. */
    val pcIsNewer: Boolean get() = pcEditedAt?.isAfter(phoneEditedAt) == true
}
