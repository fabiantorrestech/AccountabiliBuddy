package com.accountabilibuddy.companion.ui.settings

import com.accountabilibuddy.companion.ui.SettingsCategory
import com.accountabilibuddy.companion.ui.SettingsCategory.About
import com.accountabilibuddy.companion.ui.SettingsCategory.Alerts
import com.accountabilibuddy.companion.ui.SettingsCategory.AppAndData
import com.accountabilibuddy.companion.ui.SettingsCategory.Appearance
import com.accountabilibuddy.companion.ui.SettingsCategory.Connections
import com.accountabilibuddy.companion.ui.SettingsCategory.Devices
import com.accountabilibuddy.companion.ui.SettingsCategory.FocusSessions
import com.accountabilibuddy.companion.ui.SettingsCategory.Guards
import com.accountabilibuddy.companion.ui.SettingsCategory.Setup
import com.accountabilibuddy.companion.ui.SettingsCategory.Sleep
import com.accountabilibuddy.companion.ui.SettingsCategory.YourDay
import com.accountabilibuddy.companion.ui.settings.SettingsAnchors as A

/**
 * The Settings search: a fixed list of what Settings has (term, page, row), worked out in memory as the user types. Nothing is
 * stored or sent: no history, no recent searches. A result opens its page scrolled to its row ([SearchEntry.anchor], a key in the
 * page's list). Each word typed has to start a word of the title, the page's name or a keyword, or (from 3 letters) sit inside one.
 */
object SettingsSearch {
    val index: List<SearchEntry> = listOf(
        // Your day
        SearchEntry("Lunch length", YourDay, A.LUNCH, listOf("lunch", "break", "meal", "flexible break")),
        SearchEntry("Daily steps", YourDay, A.DAILY_STEPS, listOf("steps", "step count", "walk", "walking", "health connect", "goal", "track daily steps", "ring")),
        SearchEntry("Work hours and day types", YourDay, A.WORK_HOURS, listOf("work hours", "day types", "days off", "day parts", "plan", "workday")),
        SearchEntry("Calendar events in the day plan", YourDay, A.CALENDAR_EVENTS, listOf("calendar", "events", "meeting", "meetings", "keywords", "rules", "comms", "calendar permission")),

        // Sleep
        SearchEntry("Sleep times", Sleep, A.SLEEP_TIMES, listOf("sleep mode", "bedtime", "wake time", "weeknights", "weekend", "same every night", "night")),
        SearchEntry("Wake-up alarm", Sleep, A.WAKE_ALARM, listOf("alarm", "next alarm", "clock", "wake")),
        SearchEntry("Smart bedtime", Sleep, A.SMART_BEDTIME, listOf("bedtime", "health connect", "usage access")),
        SearchEntry("Bedtime reminders", Sleep, A.BEDTIME_REMINDERS, listOf("bedtime", "reminder", "notification", "alarm", "vibrate", "wind down")),
        SearchEntry("Bedtime essentials", Sleep, A.BEDTIME_ESSENTIALS, listOf("bedtime", "essentials", "allowed apps", "lockmyphone", "night")),
        SearchEntry("Health Connect", Sleep, A.HEALTH_CONNECT, listOf("measured sleep", "connect", "sleep strip")),

        // Focus sessions
        SearchEntry("Work-start nudge and takeover screen", FocusSessions, A.START_NUDGE, listOf("nudge", "start nudge", "repeat", "again every", "work card", "full-screen", "take over the screen", "display over other apps", "unlock")),
        SearchEntry("Test session", FocusSessions, A.TEST_SESSION, listOf("try", "practice", "session", "more options")),
        SearchEntry("Session length, breaks and Pomodoro", FocusSessions, A.SESSION_LENGTHS, listOf("length", "break", "breaks", "pomodoro", "set on the pc", "timer")),

        // Guards
        SearchEntry("Phone guard (AccountabiliBuddy on this phone)", Guards, A.PHONE_GUARD, listOf("master switch", "turn off", "pause", "wait", "guard", "paused", "on this phone", "keep it on")),
        SearchEntry("Phone down on unlock", Guards, A.PHONE_DOWN, listOf("unlock", "put it away", "need it for work", "emergency", "unlock friction", "pocket", "phone-free credit", "penalty", "penalties", "phone down")),
        SearchEntry("Essentials", Guards, A.ESSENTIALS, listOf("allowed apps", "focus essentials", "essentials screen", "app icons", "folders", "add an app", "hide", "parts")),
        SearchEntry("Sense where the phone is", Guards, A.PLACEMENT, listOf("phone placement", "parked", "put down", "pocket", "bag", "desk", "pickups", "pickup", "sensors", "guard")),
        SearchEntry("Emergency phone time", Guards, A.EMERGENCY, listOf("emergency", "daily minutes", "I need my phone")),
        SearchEntry("Advanced: car pause, takeover screen, nudge repeat", Guards, A.ADVANCED, listOf("pause in the car", "car", "android auto", "bluetooth", "takeover", "take over the screen", "work card", "nudge", "repeat", "again every")),
        SearchEntry("Curbox setup", Guards, A.CURBOX_SETUP, listOf("curbox", "hard mode", "focus group", "blocks back on", "re-enable", "checklist")),
        SearchEntry("LockMyPhone-Extended", Guards, A.LOCKMYPHONE, listOf("lockmyphone", "lock my phone", "hardcore", "profile", "lock for sessions", "lift the lock for lunch", "intent api", "token")),
        SearchEntry("PC profiles and rules", Guards, A.PROFILES, listOf("profiles", "rules", "pc", "focus profile")),

        // Alerts & on-screen
        SearchEntry("Timeline notification", Alerts, A.TIMELINE, listOf("notification", "widget", "home screen", "pinned", "timeline")),
        SearchEntry("Noise player", Alerts, A.NOISE, listOf("noise", "sound", "sounds", "folder", "repeat", "music", "media controls", "volume", "widget", "calls")),
        SearchEntry("Play when headphones connect", Alerts, A.HEADPHONES, listOf("headphones", "bluetooth", "wired", "headset", "only at certain times")),

        // Devices & sync
        SearchEntry("Devices in your group", Devices, A.DEVICES, listOf("devices", "remove device", "group", "linked", "last sync", "behind", "stale")),
        SearchEntry("How they sync", Devices, A.HOW_THEY_SYNC, listOf("sync", "method", "methods", "several")),
        SearchEntry("Google Drive", Devices, A.DRIVE, listOf("drive", "google", "sign in", "sync", "account")),
        SearchEntry("Shared folder", Devices, A.FOLDER, listOf("folder", "syncthing", "nextcloud", "autosync", "nas", "onedrive", "dropbox", "sync folder")),
        SearchEntry("Own relay", Devices, A.RELAY, listOf("relay", "server", "https", "pin", "certificate", "admission key", "sync")),
        SearchEntry("QR codes and sync files", Devices, A.BY_HAND, listOf("qr", "scan", "picture", "sync file", "absync", "share", "by hand")),
        SearchEntry("Show the sync light", Devices, A.SYNC_LIGHT, listOf("sync light", "light", "dot", "indicator", "only when something's wrong", "hide")),
        SearchEntry("PC this phone controls", Devices, A.PC_CONTROL, listOf("pc", "control", "start", "end early", "several pcs")),
        SearchEntry("Link this phone", Devices, A.LINK, listOf("link code", "join", "leave group", "add a device", "code")),
        SearchEntry("Restore from an older device", Devices, A.RESTORE, listOf("backup", "restore", "removed device", "history")),
        SearchEntry("Pixel Watch", Devices, A.WATCH, listOf("watch", "wear os", "link watch", "unlink", "data layer", "google's encrypted relay", "relay", "smartwatch")),

        // Connections
        SearchEntry("Beeminder", Connections, A.BEEMINDER, listOf("goals", "goal", "bee")),
        SearchEntry("Curbox", Connections, A.CURBOX, listOf("blocks", "blocking", "re-enable", "turn blocks back on", "screen time", "status", "connect to curbox")),
        SearchEntry("Other blockers", Connections, A.OTHER_BLOCKERS, listOf("blockers", "block", "focus apps", "app blockers", "appblock", "stayfocused", "freedom")),
        SearchEntry("Health Connect", Connections, A.HEALTH_CONNECT, listOf("measured sleep", "connect", "sleep strip", "health")),

        // Appearance
        SearchEntry("Theme", Appearance, A.THEME, listOf("theme", "look", "instrument", "night desk", "paper", "style", "colours")),
        SearchEntry("Light or dark", Appearance, A.LIGHT_DARK, listOf("dark", "light", "sunset", "system", "set time", "night mode", "place")),
        SearchEntry("Colour", Appearance, A.COLOUR, listOf("colour", "color", "quiet", "standard")),
        SearchEntry("Animations", Appearance, A.ANIMATIONS, listOf("animation", "motion", "speed", "fast", "slow")),
        SearchEntry("Timeline look", Appearance, A.TIMELINE_LOOK, listOf("timeline", "grayscale", "e-ink", "eink", "outlined", "widget")),
        SearchEntry("Today layout", Appearance, A.TODAY_LAYOUT, listOf("today", "arrange", "reorder", "order", "hide", "cards", "more today", "fold", "home", "steps card", "customize")),

        // App & data
        SearchEntry("Startup", AppAndData, A.STARTUP, listOf("start with the phone", "boot", "launch", "starts when opened")),
        SearchEntry("Backup", AppAndData, A.BACKUP, listOf("backup", "export", "import", "settings", "password", "auto-backup")),
        SearchEntry("Profiles from the PC (backup file)", AppAndData, A.PROFILES_FROM_PC, listOf("import", "abbackup", "backup file", "profiles", "old way")),

        // Setup
        SearchEntry("Open the setup again", Setup, A.SETUP, listOf("setup", "assistant", "first run", "onboarding", "again")),
        SearchEntry("Permissions", Setup, A.PERMISSIONS, listOf("permission", "usage access", "notifications", "overlay", "display over other apps", "health connect", "allow")),
        SearchEntry("Google Drive setup", Setup, A.DRIVE_SETUP, listOf("drive", "client", "sha-1", "package name", "google cloud")),

        // About
        SearchEntry("Version and credits", About, A.VERSION, listOf("version", "credits", "about", "what's new", "licence", "license")),
    )

    /**
     * What [query] finds, best first: entries whose title has a word the typed text starts, then the rest. A blank query finds nothing.
     * Every word typed has to match (a word that starts a word of the title, the page's name or a keyword, or, from 3 letters, appears inside one).
     */
    fun search(query: String, entries: List<SearchEntry> = index): List<SearchEntry> {
        val words = query.lowercase().split(Regex("\\s+")).filter { it.isNotEmpty() }
        if (words.isEmpty()) return emptyList()

        return entries
            .mapNotNull { entry -> score(entry, words)?.let { entry to it } }
            .sortedBy { it.second }
            .map { it.first }
    }

    // Lower is better: 0 every word starts a word of the title, 1 starts a word anywhere, 2 only sits inside something. Null: no match.
    private fun score(entry: SearchEntry, words: List<String>): Int? {
        val titleWords = splitWords(entry.title)
        val otherWords = (entry.keywords + entry.category.title).flatMap(::splitWords)
        val haystack = (listOf(entry.title, entry.category.title) + entry.keywords).joinToString(" ").lowercase()
        var worst = 0
        for (word in words) {
            val level = when {
                titleWords.any { it.startsWith(word) } -> 0
                otherWords.any { it.startsWith(word) } -> 1
                word.length >= 3 && haystack.contains(word) -> 2
                else -> return null
            }
            worst = maxOf(worst, level)
        }

        return worst
    }

    private fun splitWords(text: String): List<String> = text.lowercase().split(Regex("[^a-z0-9']+")).filter { it.isNotEmpty() }
}
