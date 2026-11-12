package com.accountabilibuddy.companion.goals

/** The Goals list's view switch (change note §4a, same as the PC's `GoalListView`): remembered on this phone. */
enum class GoalListView(val label: String) {
    /** Active goals in area blocks (the default), with a link to the paused ones in All. */
    ByArea("By area"),

    /** Active goals only, in one block, no sections. */
    Current("Current"),

    /** Every goal in one block: paused ones last, faded, with "since <date>" and a Paused pill. */
    All("All"),
}
