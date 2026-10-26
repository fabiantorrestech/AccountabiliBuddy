package com.accountabilibuddy.companion.curbox

/** One of Curbox's focus groups; [exitable] false = a running focus can't be ended from Curbox (hard mode). */
data class CurboxFocusGroup(val id: String, val name: String, val exitable: Boolean)
