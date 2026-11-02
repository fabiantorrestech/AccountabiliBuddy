package com.accountabilibuddy.companion.ui.settings

/**
 * The names of the rows a Settings page can scroll to (`settings/devices#relay`). A page gives each of its rows one of these as its
 * list key; the search index points at them. Each is 1 to 40 of `a-z0-9-` (what a route may carry).
 */
object SettingsAnchors {
    // Your day
    const val LUNCH = "lunch"
    const val WORK_HOURS = "work-hours"

    // Sleep
    const val SLEEP_TIMES = "sleep-times"
    const val WAKE_ALARM = "wake-alarm"
    const val SMART_BEDTIME = "smart-bedtime"
    const val BEDTIME_REMINDERS = "bedtime-reminders"
    const val BEDTIME_ESSENTIALS = "bedtime-essentials"
    const val HEALTH_CONNECT = "health-connect"

    // Focus sessions
    const val TEST_SESSION = "test-session"

    // Guards
    const val PHONE_GUARD = "phone-guard"
    const val ESSENTIALS = "essentials"
    const val EMERGENCY = "emergency"
    const val PLACEMENT = "placement"
    const val PROFILES = "profiles"

    // Alerts & on-screen
    const val TIMELINE = "timeline"
    const val NOISE = "noise"
    const val HEADPHONES = "headphones"

    // Devices & sync
    const val DEVICES = "devices"
    const val PC_CONTROL = "pc-control"
    const val HOW_THEY_SYNC = "how-they-sync"
    const val DRIVE = "drive"
    const val FOLDER = "folder"
    const val RELAY = "relay"
    const val BY_HAND = "by-hand"
    const val LINK = "link"
    const val RESTORE = "restore"
    const val WATCH = "watch"
    const val SYNC_LIGHT = "sync-light"

    // Connections
    const val BEEMINDER = "beeminder"
    const val CURBOX = "curbox"
    const val OTHER_BLOCKERS = "other-blockers"

    // Appearance
    const val THEME = "theme"
    const val LIGHT_DARK = "light-dark"
    const val COLOUR = "colour"
    const val ANIMATIONS = "animations"
    const val TIMELINE_LOOK = "timeline-look"

    // App & data
    const val STARTUP = "startup"
    const val BACKUP = "backup"
    const val PROFILES_FROM_PC = "profiles-from-pc"

    // Setup
    const val PERMISSIONS = "permissions"
    const val SETUP = "setup"
    const val DRIVE_SETUP = "drive-setup"

    // About
    const val VERSION = "version"
}
