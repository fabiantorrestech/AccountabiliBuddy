package com.accountabilibuddy.companion.essentials

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.MainActivity
import com.accountabilibuddy.companion.R
import com.accountabilibuddy.companion.guidance.TrustedTime
import com.accountabilibuddy.companion.notify.Channels
import com.accountabilibuddy.companion.notify.Refresher
import com.accountabilibuddy.companion.notify.TimelineNotifier
import com.accountabilibuddy.shared.session.NightHandoverEffect
import com.accountabilibuddy.shared.session.NightHandoverRule
import com.accountabilibuddy.shared.session.NightHandoverState
import com.accountabilibuddy.shared.session.NightHandoverStep
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * The night and a session on this phone (owner design 2026-11-15; the rules are the pure [NightHandoverRule]). A session of any kind takes over
 * the night, in either mode, as a session on another device always did: no night lock and no bedtime screen while it runs.
 * - When the night would start while a session runs, a reminder says so right away ("Your night routine starts when this session ends.").
 * - When the session ends during the night, a notification (and, with the app open, a card on Today) says "Night routine starts in 5 minutes"
 *   with Snooze (5 minutes each, like the bedtime reminders) and, in ProductaBuddy only, Cancel tonight (AccountabiliBuddy has no skip of a night).
 *   After the 5 minutes the night lock and the bedtime screen start as usual.
 * All times are trusted time; shown 12-hour. The state is in the preferences "nightHandover" (device-local, never synced).
 */
object NightHandover {
    private const val PREFS = "nightHandover"
    const val ACTION_SNOOZE = "com.accountabilibuddy.companion.NIGHT_HANDOVER_SNOOZE"
    const val ACTION_CANCEL = "com.accountabilibuddy.companion.NIGHT_HANDOVER_CANCEL"
    private const val REMINDER_ID = 70
    private const val COUNTDOWN_ID = 71
    private val CLOCK = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

    /** The pending start the card shows, and whether this mode offers Cancel. */
    data class Pending(val startAt: Instant, val canCancel: Boolean, val canSnooze: Boolean)

    /** The trusted [now] as the phone's local date-time (the night's schedule is local). */
    fun local(now: Instant): LocalDateTime = LocalDateTime.ofInstant(now, ZoneId.systemDefault())

    /** Looks at the night: shows the reminder or the 5-minute notice, starts the night when the 5 minutes are over. Called on every refresh and unlock. */
    @Synchronized
    fun check(context: Context, now: Instant = TrustedTime.now()) {
        val app = context.applicationContext as CompanionApp
        val step = NightHandoverRule.step(load(context), nightId(app, now), sessionRunning(app, now), now)
        apply(context, app, step)
    }

    /** True while the night is held back for a session (or the 5 minutes after it, or a cancelled night). */
    fun holds(app: CompanionApp, now: Instant): Boolean =
        NightHandoverRule.holds(load(app), nightId(app, now), sessionRunning(app, now), now)

    /** The start time while the card shows, else null. */
    fun pending(app: CompanionApp, now: Instant = TrustedTime.now()): Pending? =
        NightHandoverRule.pendingStart(load(app), nightId(app, now), sessionRunning(app, now), now)
            ?.let { Pending(it, NightHandoverRule.canCancel(EssentialsLaunch.mode(app)), NightHandoverRule.canSnooze(EssentialsLaunch.mode(app), load(app))) }

    /** Snooze: the start moves 5 minutes on. False when no card shows. */
    @Synchronized
    fun snooze(context: Context, now: Instant = TrustedTime.now()): Boolean {
        val app = context.applicationContext as CompanionApp
        val step = NightHandoverRule.snooze(EssentialsLaunch.mode(app), load(context), nightId(app, now), sessionRunning(app, now), now) ?: return false
        apply(context, app, step)
        return true
    }

    /** Cancel tonight (ProductaBuddy only). False when this mode has no skip or no card shows. */
    @Synchronized
    fun cancel(context: Context, now: Instant = TrustedTime.now()): Boolean {
        val app = context.applicationContext as CompanionApp
        val step = NightHandoverRule.cancel(EssentialsLaunch.mode(app), load(context), nightId(app, now), sessionRunning(app, now), now) ?: return false
        apply(context, app, step)
        return true
    }

    // A real session only (own or a PC's): the test session never holds the night and never hands it over.
    private fun sessionRunning(app: CompanionApp, now: Instant): Boolean = EssentialsLaunch.nightSessionRunning(app, now)

    // The night's id while the night engages (allowed in this mode and switched on), else null.
    private fun nightId(app: CompanionApp, now: Instant): String? {
        if (!EssentialsLaunch.nightAllowed(app) || !app.bedtimeEssentials.onUnlockInSession) return null
        return BedtimeNight.current(app, local(now))?.start?.toString()
    }

    private fun apply(context: Context, app: CompanionApp, step: NightHandoverStep) {
        save(context, step.state)
        step.effects.forEach { effect ->
            when (effect) {
                NightHandoverEffect.SHOW_REMINDER -> notify(context, REMINDER_ID, "Night routine", "Your night routine starts when this session ends.", emptyList())
                NightHandoverEffect.CLEAR_REMINDER -> NotificationManagerCompat.from(context).cancel(REMINDER_ID)
                NightHandoverEffect.CLEAR_COUNTDOWN -> NotificationManagerCompat.from(context).cancel(COUNTDOWN_ID)
                NightHandoverEffect.SHOW_COUNTDOWN -> step.state.startAt?.let { start ->
                    val actions = buildList {
                        if (NightHandoverRule.canSnooze(EssentialsLaunch.mode(app), step.state)) add(action(context, ACTION_SNOOZE, "Snooze"))
                        if (NightHandoverRule.canCancel(EssentialsLaunch.mode(app))) add(action(context, ACTION_CANCEL, "Cancel"))
                    }
                    notify(context, COUNTDOWN_ID, "Night routine starts in 5 minutes", "At ${CLOCK.format(local(start))}.", actions)
                }
                NightHandoverEffect.NIGHT_STARTS -> EssentialsLaunch.forgetNightLock(context)
            }
        }
        // The refresh wakes up at the start time.
        Refresher.handoverAt = step.state.startAt?.takeIf { !step.state.done && !step.state.cancelled }
    }

    private fun notify(context: Context, id: Int, title: String, text: String, actions: List<NotificationCompat.Action>) {
        if (!TimelineNotifier.canPost(context)) return
        val builder = NotificationCompat.Builder(context, Channels.BEDTIME)
            .setSmallIcon(R.drawable.ic_stat_timeline)
            .setContentTitle(title)
            .setContentText(text)
            .setOnlyAlertOnce(true)
            .setContentIntent(openApp(context))
        actions.forEach(builder::addAction)
        runCatching { NotificationManagerCompat.from(context).notify(id, builder.build()) }
    }

    private fun action(context: Context, action: String, label: String): NotificationCompat.Action {
        val intent = Intent(context, NightHandoverReceiver::class.java).setAction(action)
        val pending = PendingIntent.getBroadcast(context, action.hashCode(), intent, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        return NotificationCompat.Action.Builder(0, label, pending).build()
    }

    private fun openApp(context: Context): PendingIntent = PendingIntent.getActivity(
        context, 7071, Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
        PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
    )

    private fun load(context: Context): NightHandoverState {
        val p = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        return NightHandoverState(
            night = p.getString("night", null),
            sawSession = p.getBoolean("saw", false),
            reminderShown = p.getBoolean("reminder", false),
            startAt = p.getLong("startAt", 0L).takeIf { it > 0 }?.let(Instant::ofEpochMilli),
            done = p.getBoolean("done", false),
            cancelled = p.getBoolean("cancelled", false),
            snoozes = p.getInt("snoozes", 0),
            countdownShown = p.getBoolean("countdownShown", false),
        )
    }

    private fun save(context: Context, s: NightHandoverState) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
            .putString("night", s.night).putBoolean("saw", s.sawSession).putBoolean("reminder", s.reminderShown)
            .putLong("startAt", s.startAt?.toEpochMilli() ?: 0L).putBoolean("done", s.done).putBoolean("cancelled", s.cancelled).putInt("snoozes", s.snoozes).putBoolean("countdownShown", s.countdownShown)
            .apply()
    }
}
