package com.accountabilibuddy.companion.curbox

/** One of Curbox's groups ([kind]: focus, app blocker or keyword) and how many items it holds. */
data class CurboxGroup(val kind: String, val id: String, val name: String, val active: Boolean?, val count: Int)
