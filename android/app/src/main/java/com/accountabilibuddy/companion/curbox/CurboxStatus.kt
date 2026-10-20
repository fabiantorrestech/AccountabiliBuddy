package com.accountabilibuddy.companion.curbox

/**
 * What the phone knows about Curbox (an Android blocker that can run alongside): whether it's installed and has
 * allowed this app, and when it has, its state and groups.
 */
data class CurboxStatus(
    val installed: Boolean = false,
    val packageName: String? = null,
    val granted: Boolean = false,
    val apiVersion: Int = 0,
    val focusActive: Boolean = false,
    val focusGroup: String? = null,
    val focusRemainingMinutes: Int? = null,
    val screenTimeMinutes: Int? = null,
    val groups: List<CurboxGroup> = emptyList(),
    val error: String? = null,
)
