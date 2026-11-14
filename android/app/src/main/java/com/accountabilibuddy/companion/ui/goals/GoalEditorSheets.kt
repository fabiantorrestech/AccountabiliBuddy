package com.accountabilibuddy.companion.ui.goals

import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.Saver
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.goals.GoalSettingsPrefs
import com.accountabilibuddy.companion.goals.PhoneGoalStatus
import com.accountabilibuddy.companion.guidance.isCompanionMode
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklyResult
import com.accountabilibuddy.shared.goals.GoalDraft
import com.accountabilibuddy.shared.goals.GoalDraftText
import com.accountabilibuddy.shared.goals.GoalDrafts
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalReminder
import com.accountabilibuddy.shared.goals.GoalReminderEdits
import com.accountabilibuddy.shared.goals.GoalReviewAdjustment
import com.accountabilibuddy.shared.goals.GoalSourceRule
import java.time.OffsetDateTime
import java.time.format.DateTimeFormatter
import java.util.Locale
import java.util.UUID
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray

/**
 * New goal and Edit goal as sheets (round 2 R6b2; brief §4.3, boards Goals-Phone-New and -New-2): **step 1** is what it is (name, kind,
 * icon, colour, area), **step 2** how it's measured (target, What it takes, what counts, reminders), each an 80%
 * [com.accountabilibuddy.companion.ui.sheets.AbSheet] with a sticky footer. The same two sheets edit a goal ([goalId]; [startAtTarget]
 * opens step 2, for "Change the target" and the weekly review's Adjust; [startAtReminders] opens the Reminders page, for the goal menu's and
 * page's Reminders row). Nothing is stored until Create / Save; the half-made goal survives a screen turn (memory only).
 *
 * Rules: [GoalDrafts] (the PC's editor rules). An ordinary goal's target, raised or lowered, applies at once (it feeds no guard). On
 * Deep work the daily target is **read only** here (ADR 29: set on the PC) and the weekly target waits like any looser change when
 * lowered ([com.accountabilibuddy.companion.goals.PhoneDeepWorkWeekly]): refused during a session outside ProductaBuddy mode, never
 * applied during one, no "apply now"; raising applies at once. Saving another edit of Deep work leaves a waiting lowering alone unless
 * the weekly field itself was edited ([GoalDrafts.weeklyChangeAsked]).
 *
 * [suggested]: the weekly review's "Adjust the target" numbers, pre-filled into the draft (nothing is saved until Save).
 * Reminders (R6b3) are kept with the draft and saved with the goal ([com.accountabilibuddy.companion.goals.PhoneGoalStore.setReminders]);
 * opened on their own they save with their own Save.
 */
@Composable
fun GoalEditorSheets(
    goalId: UUID?,
    allReceived: List<ReceivedDevice>,
    received: List<ReceivedDevice>,
    onMessage: (String) -> Unit,
    onDismiss: () -> Unit,
    startAtTarget: Boolean = false,
    startAtReminders: Boolean = false,
    suggested: GoalReviewAdjustment? = null,
) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val status by app.goals.status.collectAsStateWithLifecycle()
    val lists by app.goals.lists.collectAsStateWithLifecycle()
    val weekly by app.goals.deepWork.state.collectAsStateWithLifecycle()
    val reminderDefaults by GoalSettingsPrefs.reminders.collectAsStateWithLifecycle()
    val productaBuddy = isCompanionMode()
    val stored = goalId?.let { id -> lists.liveGoals.firstOrNull { it.id == id } }

    // A goal that is gone (deleted elsewhere), or goals that can't be used, close the sheet.
    LaunchedEffect(status, stored == null) {
        if (status != PhoneGoalStatus.Ready || (goalId != null && stored == null)) onDismiss()
    }
    if (status != PhoneGoalStatus.Ready || (goalId != null && stored == null)) return

    var draft by rememberSaveable(goalId, stateSaver = DraftSaver) {
        mutableStateOf(
            if (stored != null) GoalDrafts.withAdjustment(GoalDrafts.fromGoal(stored, weekly.minutes), suggested)
            else GoalDrafts.newDraft(UUID.randomUUID(), lists.liveAreas.firstOrNull()?.id),
        )
    }
    // R6 milestone carry (f81bb32b item 2): whether the Deep work weekly field was touched in this sheet.
    var weeklyEdited by rememberSaveable(goalId) { mutableStateOf(false) }
    val storedReminders = remember(goalId) { lists.reminders.filter { it.goalId == goalId && it.isLive }.sortedBy { it.position } }
    var reminders by rememberSaveable(goalId, stateSaver = RemindersSaver) { mutableStateOf(storedReminders) }
    val startRules = remember(goalId) { GoalReminderEdits.rulesOf(storedReminders, reminderDefaults, productaBuddy) }
    var skipWhenDone by rememberSaveable(goalId) { mutableStateOf(startRules.first) }
    var nudgeWhenBehind by rememberSaveable(goalId) { mutableStateOf(startRules.second) }
    var step by rememberSaveable(goalId) { mutableStateOf(if ((startAtTarget || startAtReminders) && stored != null) 2 else 1) }
    var sourcesOpen by rememberSaveable { mutableStateOf(false) }
    var remindersOpen by rememberSaveable(goalId) { mutableStateOf(startAtReminders && stored != null) }
    var newAreaOpen by rememberSaveable { mutableStateOf(false) }
    val title = if (goalId == null) "New goal" else "Edit goal"
    val onDraft: (GoalDraft) -> Unit = { next ->
        if (next.weeklyHours != draft.weeklyHours) weeklyEdited = true
        draft = next
    }

    if (newAreaOpen) {
        GoalAreaSheet(areaId = null, onDismiss = { newAreaOpen = false }, onMessage = onMessage, onSaved = { id -> draft = draft.copy(areaId = id) })
        // The goal's sheet stays behind it; nothing more to draw until it closes.
        return
    }

    if (remindersOpen) {
        // Opened on its own (the goal menu's or page's Reminders): Save keeps the reminders only. From step 2: Done goes back to it.
        val standalone = startAtReminders && stored != null
        GoalRemindersSheet(
            goalId = draft.id, goalName = draft.name, reminders = reminders, onReminders = { reminders = it },
            skipWhenDone = skipWhenDone, nudgeWhenBehind = nudgeWhenBehind,
            onRules = { skip, nudge -> skipWhenDone = skip; nudgeWhenBehind = nudge },
            productaBuddy = productaBuddy,
            doneLabel = if (standalone) "Save" else "Done",
            onDone = {
                if (standalone) {
                    val error = app.goals.setReminders(draft.id, GoalReminderEdits.withRules(reminders, skipWhenDone, nudgeWhenBehind))
                    if (error == null) onDismiss() else onMessage(error)
                } else {
                    remindersOpen = false
                }
            },
            onDismiss = onDismiss,
            onBack = if (standalone) null else ({ remindersOpen = false }),
        )
        return
    }

    if (step == 1) {
        GoalBasicsStep(
            title = title, draft = draft, onDraft = onDraft, areas = lists.liveAreas,
            onNewArea = { newAreaOpen = true }, onNext = { step = 2 }, onDismiss = onDismiss,
        )
        return
    }

    // ADR 89: another device's device-only labels aren't offered or named; a source already on one reads "Uses a label from <device>".
    val scopeView = com.accountabilibuddy.companion.ui.tasks.rememberScopeView(app)
    val allLabelNames = remember(lists) { app.sharedLists.allLabels() }
    val labels = remember(allLabelNames, scopeView) { com.accountabilibuddy.shared.tasks.GoalLabelScopes.pickable(allLabelNames, scopeView) }
    val steps by app.steps.state.collectAsStateWithLifecycle()
    if (sourcesOpen) {
        GoalSourcesSheet(
            current = draft.sources, labels = labels, stepsOn = steps.enabled,
            onAdd = { rule: GoalSourceRule -> draft = draft.copy(sources = draft.sources + rule) },
            onDismiss = onDismiss, onBack = { sourcesOpen = false },
        )
        return
    }

    GoalTargetStep(
        title = title, draft = draft, onDraft = onDraft, stored = stored, weekly = weekly, labels = labels,
        labelNote = { id -> com.accountabilibuddy.shared.tasks.GoalLabelScopes.hiddenNote(id, allLabelNames, scopeView) { device -> com.accountabilibuddy.companion.ui.tasks.scopeDeviceName(app, device) } },
        allReceived = allReceived, received = received, areaName = lists.liveAreas.firstOrNull { it.id == draft.areaId }?.name,
        onSources = { sourcesOpen = true },
        onCancelWeekly = { app.goals.deepWork.cancel() },
        reminders = reminders,
        onOpenReminders = { remindersOpen = true },
        onAddReminder = {
            reminders = reminders + GoalReminderEdits.newReminder(draft.id, reminders.size + 1.0, skipWhenDone, nudgeWhenBehind, OffsetDateTime.now())
            remindersOpen = true
        },
        suggestedNote = if (suggested != null && stored != null) adjustNote(stored.isDeepWork, com.accountabilibuddy.companion.goals.PhoneDeepWork.hasPc(received.map { it.file })) else null,
        onBack = { step = 1 }, onDismiss = onDismiss,
        onSave = save@{
            val error = save(app, draft, stored, weeklyEdited, GoalReminderEdits.withRules(reminders, skipWhenDone, nudgeWhenBehind), onMessage)
            if (error == null) onDismiss() else onMessage(error)
        },
    )
}

private fun adjustNote(deepWork: Boolean, hasPc: Boolean = true): String =
    if (deepWork) "From the weekly review: Deep work's daily target is set " + (if (hasPc) "on your PC" else "below") + "; its weekly target is below."
    else "From the weekly review: the suggested numbers are filled in. Nothing changes until you save."

// Makes the goal and keeps it with its reminders; returns the reason it couldn't (the screen shows it), or null when saved.
private fun save(
    app: CompanionApp,
    draft: GoalDraft,
    stored: com.accountabilibuddy.shared.goals.Goal?,
    weeklyEdited: Boolean,
    reminders: List<GoalReminder>,
    onMessage: (String) -> Unit,
): String? {
    val lists = app.goals.lists.value
    val position = (lists.liveGoals.filter { it.areaId == draft.areaId }.maxOfOrNull { it.position } ?: 0.0) + 1.0
    val goal = GoalDrafts.toGoal(draft, stored, OffsetDateTime.now(), position) ?: return GoalDrafts.basicsProblem(draft) ?: GoalDrafts.targetProblem(draft) ?: GoalDrafts.sourcesProblem(draft) ?: "That goal can't be saved."
    val error = if (stored == null) app.goals.addGoal(goal) else app.goals.updateGoal(goal)
    if (error != null) return error
    app.goals.setReminders(goal.id, reminders.map { it.copy(goalId = goal.id) })?.let { return "The goal was saved, but its reminders weren't: $it" }
    if (stored != null && stored.isDeepWork) {
        val wanted = GoalDrafts.deepWorkWeeklyMinutes(draft)
        val current = app.goals.deepWork.current()
        // A different amount, or the current one typed back while a lowering waits (that drops the wait, the PC's "Unchanged");
        // saving another edit with the weekly field untouched leaves a waiting lowering alone.
        if (GoalDrafts.weeklyChangeAsked(wanted, current.minutes, current.pending != null, weeklyEdited)) {
            when (app.goals.deepWork.set(wanted)) {
                GoalDeepWorkWeeklyResult.Waiting -> onMessage(waitingText(app))
                GoalDeepWorkWeeklyResult.RefusedDuringSession ->
                    return "The rest was saved. Lowering the weekly target isn't possible during a focus session (raising it is)."
                else -> Unit
            }
        }
    }
    return null
}

private val CLOCK: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

/** "The weekly target lowers at 2:14 PM (never during a session). Cancel is on the goal's page." */
internal fun waitingText(app: CompanionApp): String {
    val pending = app.goals.deepWork.state.value.pending ?: return "Lowering the weekly target waits like any looser change."
    val at = pending.appliesAt.atZoneSameInstant(java.time.ZoneId.systemDefault()).format(CLOCK)
    val to = pending.amount?.let { com.accountabilibuddy.shared.goals.GoalTexts.minutes(it) } ?: "no target"
    return "The weekly target lowers to $to at $at, and never during a session. Cancel is on the goal's page."
}

private val DraftSaver = Saver<GoalDraft, String>(save = { GoalDraftText.write(it) }, restore = { GoalDraftText.read(it) })

// The reminders being edited, kept across a screen turn in the PC's JSON (memory of the running app only; reasons are private text).
private val RemindersSaver = Saver<List<GoalReminder>, String>(
    save = { GoalJson.writeReminders(it).toString() },
    restore = { text -> runCatching { GoalJson.readReminders(Json.parseToJsonElement(text) as JsonArray) }.getOrNull() },
)
