package com.accountabilibuddy.companion.ui.goals

import com.accountabilibuddy.shared.goals.GoalArea
import com.accountabilibuddy.shared.goals.GoalFacts

/**
 * One block of the Goals list: an area with its header (By area), or the one block without a header (Current, All).
 *
 * @property collapsed Folded on this phone (By area only): the header shows, the rows don't.
 */
data class GoalListSection(val area: GoalArea?, val rows: List<GoalFacts>, val collapsed: Boolean)
