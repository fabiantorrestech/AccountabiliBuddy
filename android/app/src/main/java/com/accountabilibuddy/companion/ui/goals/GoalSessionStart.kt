package com.accountabilibuddy.companion.ui.goals

/**
 * What the goal menu's "Start a session…" needs (brief §4.3, change note §4a; PS3a): why a session can't be started from this phone now ([blocked]; null when it
 * can), and the start itself. The start is the same Start sheet Today's Start button opens ([com.accountabilibuddy.companion.ui.today.StartSessionSheetHost]:
 * "On <PC>" or "On this phone", R16), so there is one start and it never goes around a wait, a lock or a running session (R1). With no PC the sheet offers only
 * "On this phone" (R15); the old "Phone sessions are coming" is gone.
 */
class GoalSessionStart(
    val blocked: String?,
    /** Opens the Start sheet over the Goals place (the caller shows it; the menu closes first). */
    val onOpenSheet: () -> Unit,
)
