package com.accountabilibuddy.shared.goals

/**
 * The goal page's day panel keeps ONE height whatever day is picked and whatever the goal is (PH1, owner 2026-11-13: tapping Today and
 * then other days made the page shift). The panel reserves its tallest variant: the title and value line beside a 48 dp action slot
 * (Tick it, Untick or Edit, empty when there is none), the source line at two lines, and a 44 dp "Back to today" slot (empty on today).
 * Every slot is always there, so which variant shows never changes the height; the value-by-hand form fits inside the same height.
 * The heights are density-independent pixels at the default text size.
 */
object GoalDayPanelSlots {
    const val PADDING_DP = 20
    const val ACTION_SLOT_DP = 48
    const val SOURCE_LINES = 2
    const val SOURCE_LINE_DP = 17
    const val BACK_SLOT_DP = 44
    const val GAP_DP = 4
    const val FIELD_DP = 56

    /** The height every panel reserves, whatever [panel] says. */
    @Suppress("UNUSED_PARAMETER")
    fun reservedDp(panel: GoalDayPanel): Int = RESERVED_DP

    /** The reserved height itself: padding + action slot + gap + source lines + gap + back slot. */
    const val RESERVED_DP = PADDING_DP + ACTION_SLOT_DP + GAP_DP + SOURCE_LINES * SOURCE_LINE_DP + GAP_DP + BACK_SLOT_DP

    /** The form to type a value (field, then Save and Cancel in a 48 dp row) with the same padding and gap. */
    const val EDIT_FORM_DP = PADDING_DP + FIELD_DP + GAP_DP + ACTION_SLOT_DP

    /** Whether the value-by-hand form fits in the reserved height (it must, so editing never moves what is below). */
    fun editFits(): Boolean = EDIT_FORM_DP <= RESERVED_DP
}
