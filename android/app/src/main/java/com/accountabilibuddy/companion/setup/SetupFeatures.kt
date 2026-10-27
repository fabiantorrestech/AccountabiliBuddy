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
    private const val PREFS = "setupFeatures"
    private const val VERSIONS = "versions"

    val all = listOf(
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
