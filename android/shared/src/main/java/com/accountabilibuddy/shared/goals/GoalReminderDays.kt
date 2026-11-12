package com.accountabilibuddy.shared.goals

/** Which days a goal reminder rings on (brief §5.1). */
enum class GoalReminderDays {
    /** Today only ([GoalReminder.date] holds the day it was set for). */
    Today,

    /** The weekdays in [GoalReminder.weekdays]. */
    Weekdays,

    /** One date ([GoalReminder.date]). */
    Date,

    EveryDay,
}
