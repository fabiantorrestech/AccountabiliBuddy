package com.accountabilibuddy.shared.goals

/** The review's snooze choices (board Goals-Phone-Review's menu). */
enum class GoalReviewSnooze {
    /** "Tomorrow · Mon, Nov 16 · 9:00 AM". */
    Tomorrow,

    /** "In 2 days · Tue, Nov 17 · 9:00 AM". */
    TwoDays,

    /** "Nudges… · Remind me once · then quiet": tomorrow at 9:00 AM, one notification then, and none again this week. */
    RemindOnce,
}
