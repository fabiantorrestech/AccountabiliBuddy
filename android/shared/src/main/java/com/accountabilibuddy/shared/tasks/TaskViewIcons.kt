package com.accountabilibuddy.shared.tasks

/**
 * The icons a saved view can have (the PC's `ViewEditorViewModel.IconNames`, name for name; ADR 32: the same meaning gets the same
 * icon on both). A view stores the Material icon's name ("lightning-bolt"); no name, or one this app doesn't know (a newer app's),
 * shows the bookmark.
 */
object TaskViewIcons {
    const val DEFAULT = "bookmark-outline"

    /** What the picker offers, in the PC's order. */
    val NAMES: List<String> = listOf(
        DEFAULT, "star-outline", "lightning-bolt", "head-cog", "briefcase-outline", "home-outline", "calendar-today", "fire", "leaf", "school-outline",
    )

    // The built-in views' own names ("star") and the plain bookmark are drawn too, though the picker doesn't offer them.
    private val DRAWN: Set<String> = NAMES.toSet() + setOf("star", "bookmark")

    /** The name to draw for [name]: itself when known, else the bookmark. */
    fun drawn(name: String?): String = name?.trim()?.takeIf { it in DRAWN } ?: DEFAULT

    /** The picker's choice for a view that has [name]: the bookmark when it has none or an unknown one. */
    fun picked(name: String?): String = drawn(name)
}
