package com.accountabilibuddy.companion.backup

/**
 * What a restore can leave out (Settings → Backup, and the first setup page): each group's preference files and settings files. What belongs
 * to no group (startup, pause) always comes back. Reasons, notes, sign-ins and sealed tokens are never in a backup, so no group holds them.
 */
enum class BackupGroup(
    val title: String,
    val description: String,
    val prefs: Set<String> = emptySet(),
    val files: Set<String> = emptySet(),
) {
    FocusLimits(
        "Allowed apps and focus limits", "Essentials, bedtime essentials, emergency time and the Phone down penalties",
        prefs = setOf("essentials", "bedtimeEssentials", "emergency", "phoneDown"),
    ),
    WorkCard("Work card and nudges", "The full-screen work card and the start nudge", prefs = setOf("takeover", "nudges")),
    Sleep(
        "Sleep and bedtime", "Sleep times, reminders, smart bedtime, lunch length and the daily steps switch and goal",
        prefs = setOf("smartBedtime", "lunchSettings", "stepsSettings"), files = setOf("sleep.json", "phone.json"),
    ),
    Noise("Noise and headphones", "The noise player and its headphone rules", prefs = setOf("noise", "headphones")),
    Look("Look", "Theme, motion, the bar's style, the layout of Today and how Goals are shown", prefs = setOf("ui", "goalsUi")),
    SyncSettings(
        "Sync settings", "Which ways your devices sync, and the linked watch. Folders may need choosing again",
        files = setOf("sync-settings.json", "watch.json"),
    ),
    Timers("Timers and alarms", "Your timers and alarms on this phone", prefs = setOf("timers")),
    Curbox("Curbox and blocks", "Curbox hard mode, turning blocks back on and the lunch lift", prefs = setOf("curbox", "blockGuard", "lunchLockLift")),
    GuardHistory(
        "Guard history", "Times of emergency and work uses, Phone down and today's counts. Reasons and notes are never in a backup",
    ),
    ;

    companion object {
        /** The group a preference file belongs to; null for one that always comes back. */
        fun ofPrefs(name: String): BackupGroup? = entries.firstOrNull { name in it.prefs }

        /** The group a settings file belongs to; null for one that always comes back. */
        fun ofFile(name: String): BackupGroup? = entries.firstOrNull { name in it.files }
    }
}
