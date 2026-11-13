package com.accountabilibuddy.companion.setup

import android.content.Context
import com.accountabilibuddy.companion.curbox.CurboxClient

/** A feature that needs setting up on the phone, for the migration assistant ("What's new"). */
data class SetupFeature(
    val id: String,
    val since: String,
    val title: String,
    val relevant: (Context) -> Boolean,
    val setUp: (Context) -> Boolean,
)

/**
 * Every feature a user may need to set up after an upgrade (project rule: a new or reworked feature that needs
 * setting up gets an entry here with the version it arrives in, its "set up?" check and its setup page). The
 * migration assistant (5 taps on the logo on About; a quiet Today card after an upgrade) runs the pages of the ones
 * not set up yet. Preferences "setupFeatures" (done, card dismissed for a version) and "versions" (last, previous).
 */
object SetupFeatures {
    const val CURBOX = "curbox.setup"
    const val BEDTIME = "bedtime.essentials"
    const val PLACEMENT = "phone.placement"
    const val PHONE_DOWN = "phone.down"
    const val RELAY = "sync.relay"
    const val DAY_PLAN_MODES = "dayplan.modes"
    const val APPEARANCE = "appearance.themes"
    const val CALENDAR_EVENTS = "calendar.events"
    const val DAILY_STEPS = "steps.daily"
    const val AUTOSYNC_NUDGE = "sync.autosyncNudge"
    const val FINISHED_WORK_TAG = "nudge.finishedWorkTag"
    const val GUIDANCE = "guidance.mode"
    const val GOALS_REVIEW = "goals.review"
    const val STEPS_IN_GOALS = "steps.inGoals"
    const val TASKS_REDESIGN = "tasks.redesign"
    const val TASK_RING_DEVICES = "tasks.ringDevices"
    private const val PREFS = "setupFeatures"
    private const val VERSIONS = "versions"

    val all = listOf(
        SetupFeature(
            TASKS_REDESIGN, "0.1.79", "Tasks: pages per bin, label and tag, labels, saved views and the view the page opens with",
            // Only once the one task list is in use; set up once seen in the assistant, or a default view was chosen for the account.
            relevant = { (it.applicationContext as com.accountabilibuddy.companion.CompanionApp).taskModel.isActive },
            setUp = {
                val app = it.applicationContext as com.accountabilibuddy.companion.CompanionApp
                done(it, TASKS_REDESIGN) || com.accountabilibuddy.shared.tasks.TaskAccountSettingsEdits.isChosen(app.taskModel.lists.value.account?.defaultViewStamp)
            },
        ),
        SetupFeature(
            TASK_RING_DEVICES, "0.1.79", "Task reminders: which of your devices ring (a PC always rings for its own)",
            // Only with another phone in the group (a PC alone always rings, a lone phone has nothing to choose); set up once seen, or the list was chosen.
            relevant = {
                val app = it.applicationContext as com.accountabilibuddy.companion.CompanionApp
                app.taskModel.isActive && com.accountabilibuddy.companion.tasks.TaskSettingsActions(app).phones().size > 1
            },
            setUp = {
                val app = it.applicationContext as com.accountabilibuddy.companion.CompanionApp
                done(it, TASK_RING_DEVICES) || com.accountabilibuddy.shared.tasks.TaskAccountSettingsEdits.isChosen(app.taskModel.lists.value.account?.ringOnStamp)
            },
        ),
        SetupFeature(
            STEPS_IN_GOALS, "0.1.78", "Steps now live in Goals: add a steps goal in place of the Today ring",
            // Only for a phone whose Today showed steps and that still tracks them with no steps goal; set up once added, closed or seen.
            relevant = {
                val app = it.applicationContext as com.accountabilibuddy.companion.CompanionApp
                com.accountabilibuddy.companion.ui.UiPrefs.stepsMoved.value == com.accountabilibuddy.companion.today.StepsMovedNote.Show &&
                    app.steps.state.value.enabled && !com.accountabilibuddy.shared.goals.GoalStepsRules.hasStepsGoal(app.goals.lists.value.liveGoals)
            },
            setUp = { done(it, STEPS_IN_GOALS) || com.accountabilibuddy.companion.ui.UiPrefs.stepsMoved.value == com.accountabilibuddy.companion.today.StepsMovedNote.Done },
        ),
        SetupFeature(
            GOALS_REVIEW, "0.1.78", "Goals: the weekly goals review and goal reminders on this phone",
            // Only once the goals can be used; set up once seen in the assistant, or the review's day was chosen in Settings › Goals.
            relevant = { (it.applicationContext as com.accountabilibuddy.companion.CompanionApp).goals.status.value == com.accountabilibuddy.companion.goals.PhoneGoalStatus.Ready },
            setUp = { done(it, GOALS_REVIEW) || com.accountabilibuddy.companion.goals.GoalSettingsPrefs.review.value.day != null },
        ),
        SetupFeature(
            GUIDANCE, "0.1.77", "ProductaBuddy: a companion without the guarding, on all your devices",
            relevant = { true },
            // Set up once chosen here or on another device (a waiting switch counts).
            setUp = { done(it, GUIDANCE) || (it.applicationContext as com.accountabilibuddy.companion.CompanionApp).guidance.state.value.let { state -> state.isChosen || state.pending != null } },
        ),
        SetupFeature(
            CURBOX, "0.1.58", "Curbox: guided setup, when blocks come back on, hard mode",
            relevant = { CurboxClient(it).findPackage() != null },
            setUp = { done(it, CURBOX) },
        ),
        SetupFeature(
            BEDTIME, "0.1.59", "Bedtime essentials: the night's own apps and LockMyPhone-Extended lock",
            relevant = { true },
            setUp = { done(it, BEDTIME) },
        ),
        SetupFeature(
            PLACEMENT, "0.1.61", "Phone placement: the PC knows if the phone is in a bag, face down or out (needs the timeline notification)",
            relevant = { true },
            // Set up once seen in the assistant, or the timeline notification is on (the detector runs in its service).
            setUp = { done(it, PLACEMENT) || (it.applicationContext as com.accountabilibuddy.companion.CompanionApp).store.state.value.timelineNotificationOn },
        ),
        SetupFeature(
            PHONE_DOWN, "0.1.63", "Phone down screen: unlocking in a PC session shows \"Phones down\", with pocket penalties",
            relevant = { true },
            setUp = { done(it, PHONE_DOWN) },
        ),
        SetupFeature(
            RELAY, "0.1.62", "Own relay: sync through a small relay you run yourself, in about a second",
            relevant = { true },
            // Set up once seen in the assistant, or the relay is on with an address (a join QR from a PC sets it).
            setUp = {
                val settings = (it.applicationContext as com.accountabilibuddy.companion.CompanionApp).sync.settings.value
                done(it, RELAY) || (settings.relayOn && settings.relayUrl != null)
            },
        ),
        SetupFeature(
            DAY_PLAN_MODES, "0.1.62", "Day plan modes: day types, a week map with A/B weeks, work window, Budget and Free days",
            relevant = { true },
            // Set up once seen in the assistant, or the plan already uses something new (a third day type, a mode, a window or week B).
            setUp = {
                val plan = (it.applicationContext as com.accountabilibuddy.companion.CompanionApp).store.state.value.document.dayPlan
                done(it, DAY_PLAN_MODES) || plan.templates.size > 2 || plan.cycle.weeks.size > 1 ||
                    plan.templates.any { named -> named.template.mode != com.accountabilibuddy.companion.model.DayMode.Schedule || named.template.workWindow != null }
            },
        ),
        SetupFeature(
            APPEARANCE, "0.1.67", "Appearance: three themes, light or dark by the clock or by sunset, and quiet colour",
            relevant = { true },
            // Set up once seen in the assistant, or a theme was already picked in Settings › Appearance.
            setUp = { done(it, APPEARANCE) || it.getSharedPreferences("ui", Context.MODE_PRIVATE).contains("themeName") },
        ),
        SetupFeature(
            CALENDAR_EVENTS, "0.1.73", "Calendar events in the day plan: events give the guards a category; asks to read the calendar when turned on",
            relevant = { true },
            // Set up once seen in the assistant, or the switch is already on (Settings › Your day). Reading the prefs file is a tiny disk read.
            setUp = { done(it, CALENDAR_EVENTS) || (it.applicationContext as com.accountabilibuddy.companion.CompanionApp).calendarRules.rules.value.active.enabled },
        ),
        SetupFeature(
            DAILY_STEPS, "0.1.73", "Daily steps: a ring on Today to your step goal from Health Connect; asks for the steps permission when turned on",
            relevant = { true },
            // Set up once seen in the assistant, or the switch is already on (Settings › Your day).
            setUp = { done(it, DAILY_STEPS) || (it.applicationContext as com.accountabilibuddy.companion.CompanionApp).steps.state.value.enabled },
        ),
        SetupFeature(
            AUTOSYNC_NUDGE, "0.1.73", "Tell Autosync to sync: lunch timers reach your other devices sooner (needs Automation switched on in Autosync)",
            // Only for a phone that syncs through the shared folder; set up once seen in the assistant, or the switch is on.
            relevant = { (it.applicationContext as com.accountabilibuddy.companion.CompanionApp).sync.settings.value.folderOn },
            setUp = { done(it, AUTOSYNC_NUDGE) || (it.applicationContext as com.accountabilibuddy.companion.CompanionApp).autosyncNudger.isReady() },
        ),
        SetupFeature(
            FINISHED_WORK_TAG, "0.1.77", "Finished work for the day: tap an NFC tag in your car to end today's work, or wait 10 minutes",
            // Only with the full-screen work card on and NFC on the phone; set up once seen in the assistant, or a tag is registered.
            relevant = {
                com.accountabilibuddy.companion.nudge.NudgeStore(it).load().takeoverOn &&
                    android.nfc.NfcAdapter.getDefaultAdapter(it) != null
            },
            setUp = { done(it, FINISHED_WORK_TAG) || com.accountabilibuddy.companion.nfc.WorkTagStore(it).isSet },
        ),
    )

    fun pending(context: Context): List<SetupFeature> = all.filter { it.relevant(context) && !it.setUp(context) }

    /** Pending features that arrived after the version this phone ran before (none on a fresh install). */
    fun newSinceUpgrade(context: Context): List<SetupFeature> {
        val previous = versions(context).getString("previous", null) ?: return emptyList()
        return pending(context).filter { newer(it.since, previous) }
    }

    fun showsCard(context: Context, appVersion: String): Boolean =
        prefs(context).getString("cardDismissedFor", null) != appVersion && newSinceUpgrade(context).isNotEmpty()

    fun dismissCard(context: Context, appVersion: String) = prefs(context).edit().putString("cardDismissedFor", appVersion).apply()

    fun markDone(context: Context, id: String) = prefs(context).edit().putBoolean("done.$id", true).apply()

    fun done(context: Context, id: String): Boolean = prefs(context).getBoolean("done.$id", false)

    /**
     * At app start: when the version changed, the old one becomes "previous". A first start with no saved version is
     * a fresh install unless the first-run setup was already shown (an upgrade from before versions were kept).
     */
    fun recordLaunch(context: Context, appVersion: String, setupShownBefore: Boolean) {
        val prefs = versions(context)
        val last = prefs.getString("last", null)
        if (last == appVersion) return
        val previous = last ?: if (setupShownBefore) "0.1.57" else null
        prefs.edit().putString("last", appVersion).putString("previous", previous).apply()
    }

    private fun newer(version: String, than: String): Boolean {
        val a = version.split('.').map { it.toIntOrNull() ?: 0 }
        val b = than.split('.').map { it.toIntOrNull() ?: 0 }
        for (i in 0 until maxOf(a.size, b.size)) {
            val x = a.getOrElse(i) { 0 }
            val y = b.getOrElse(i) { 0 }
            if (x != y) return x > y
        }
        return false
    }

    private fun prefs(context: Context) = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    private fun versions(context: Context) = context.getSharedPreferences(VERSIONS, Context.MODE_PRIVATE)
}
