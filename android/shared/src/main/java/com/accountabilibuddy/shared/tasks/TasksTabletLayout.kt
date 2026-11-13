package com.accountabilibuddy.shared.tasks

/**
 * R7.5: how the phone's Tasks tab (the pages per bin, label or tag; the Today strip; the drag zones) is sized on a tablet. The owner's
 * call (2026-11-14): a tablet gets the phone layout, made to fit the width, never the PC's columns and never Today and the Shelf side
 * by side. Pure sizes in dp; the screen reads them.
 *
 * A tablet is a device whose smallest width is [TABLET_MIN_DP] or more, in both orientations (a phone turned sideways is wide but
 * not a tablet). The content keeps a readable width ([CONTENT_MAX_DP]) centred in the pane, the peeks are larger and their
 * names a size up, so a finger finds them.
 */
object TasksTabletLayout {
    const val TABLET_MIN_DP = 600

    /** The widest the Tasks content grows: header, strip, chips, card and peeks together. */
    const val CONTENT_MAX_DP = 720

    const val PHONE_PEEK_DP = 24
    const val TABLET_PEEK_DP = 40

    /** The pager's gap between a peek and the card, and its side padding when peeks show (a lone page has 16). */
    const val PEEK_GAP_DP = 8
    const val PEEK_SIDE_DP = 6
    const val LONE_SIDE_DP = 16

    fun isTablet(smallestWidthDp: Int): Boolean = smallestWidthDp >= TABLET_MIN_DP

    /** The width of the Tasks content: the whole pane on a phone, at most [CONTENT_MAX_DP] on a tablet. */
    fun contentWidthDp(paneWidthDp: Int, tablet: Boolean): Int = if (tablet) minOf(paneWidthDp, CONTENT_MAX_DP) else paneWidthDp

    fun peekWidthDp(tablet: Boolean): Int = if (tablet) TABLET_PEEK_DP else PHONE_PEEK_DP

    /** The peek's page name (sp) and its marker icon (dp). */
    fun peekNameSp(tablet: Boolean): Int = if (tablet) 14 else 12
    fun peekIconDp(tablet: Boolean): Int = if (tablet) 18 else 15

    /** The card's width between two peeks (a side with no neighbour keeps its peek's room, so the card never changes width). */
    fun cardWidthDp(paneWidthDp: Int, tablet: Boolean, pages: Int): Int {
        val content = contentWidthDp(paneWidthDp, tablet)
        if (pages <= 1) return content - 2 * LONE_SIDE_DP
        return content - 2 * PEEK_SIDE_DP - 2 * peekWidthDp(tablet) - 2 * PEEK_GAP_DP
    }
}
