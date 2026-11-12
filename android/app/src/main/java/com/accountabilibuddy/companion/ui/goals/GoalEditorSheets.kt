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
import com.accountabilibuddy.companion.goals.PhoneGoalStatus
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklyResult
import com.accountabilibuddy.shared.goals.GoalDraft
import com.accountabilibuddy.shared.goals.GoalDraftText
import com.accountabilibuddy.shared.goals.GoalDrafts
import com.accountabilibuddy.shared.goals.GoalSourceRule
import java.time.OffsetDateTime
import java.time.format.DateTimeFormatter
import java.util.Locale
import java.util.UUID

/**
 * New goal and Edit goal as sheets (round 2 R6b2; brief §4.3, boards Goals-Phone-New and -New-2): **step 1** is what it is (name, kind,
 * icon, colour, area), **step 2** how it's measured (target, What it takes, what counts), each an 80% [com.accountabilibuddy.companion.ui.sheets.AbSheet]
 * with a sticky footer. The same two sheets edit a goal ([goalId]; [startAtTarget] opens step 2, for "Change the target"). Nothing is
 * stored until Create / Save; the half-made goal survives a screen turn (memory only).
 *
 * Rules: [GoalDrafts] (the PC's editor rules). An ordinary goal's target, raised or lowered, applies at once (it feeds no guard). On
 * Deep work the daily target is **read only** here (ADR 29: set on the PC) and the weekly target waits like any looser change when
 * lowered ([com.accountabilibuddy.companion.goals.PhoneDeepWorkWeekly]): refused during a session outside ProductaBuddy mode, never
 * applied during one, no "apply now"; raising applies at once. Reminders are the next update's (their row says so).
 */
@Composable
fun GoalEditorSheets(
    goalId: UUID?,
    allReceived: List<ReceivedDevice>,
    received: List<ReceivedDevice>,
    onMessage: (String) -> Unit,
    onDismiss: () -> Unit,
    startAtTarget: Boolean = false,
) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val status by app.goals.status.collectAsStateWithLifecycle()
    val lists by app.goals.lists.collectAsStateWithLifecycle()
    val weekly by app.goals.deepWork.state.collectAsStateWithLifecycle()
    val stored = goalId?.let { id -> lists.liveGoals.firstOrNull { it.id == id } }

    // A goal that is gone (deleted elsewhere), or goals that can't be used, close the sheet.
    LaunchedEffect(status, stored == null) {
        if (status != PhoneGoalStatus.Ready || (goalId != null && stored == null)) onDismiss()
    }
    if (status != PhoneGoalStatus.Ready || (goalId != null && stored == null)) return

    var draft by rememberSaveable(goalId, stateSaver = DraftSaver) {
        mutableStateOf(
            if (stored != null) GoalDrafts.fromGoal(stored, weekly.minutes)
            else GoalDrafts.newDraft(UUID.randomUUID(), lists.liveAreas.firstOrNull()?.id),
        )
    }
    var step by rememberSaveable(goalId) { mutableStateOf(if (startAtTarget && stored != null) 2 else 1) }
    var sourcesOpen by rememberSaveable { mutableStateOf(false) }
    var newAreaOpen by rememberSaveable { mutableStateOf(false) }
    val title = if (goalId == null) "New goal" else "Edit goal"

    if (newAreaOpen) {
        GoalAreaSheet(areaId = null, onDismiss = { newAreaOpen = false }, onMessage = onMessage, onSaved = { id -> draft = draft.copy(areaId = id) })
        // The goal's sheet stays behind it; nothing more to draw until it closes.
        return
    }

    if (step == 1) {
        GoalBasicsStep(
            title = title, draft = draft, onDraft = { draft = it }, areas = lists.liveAreas,
            onNewArea = { newAreaOpen = true }, onNext = { step = 2 }, onDismiss = onDismiss,
        )
        return
    }

    val labels = remember(lists) { app.sharedLists.allLabels() }
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
        title = title, draft = draft, onDraft = { draft = it }, stored = stored, weekly = weekly, labels = labels,
        allReceived = allReceived, received = received, areaName = lists.liveAreas.firstOrNull { it.id == draft.areaId }?.name,
        onSources = { sourcesOpen = true },
        onCancelWeekly = { app.goals.deepWork.cancel() },
        onBack = { step = 1 }, onDismiss = onDismiss,
        onSave = save@{
            val error = save(app, draft, stored, onMessage)
            if (error == null) onDismiss() else onMessage(error)
        },
    )
}

// Makes the goal and keeps it; returns the reason it couldn't (the screen shows it), or null when saved.
private fun save(app: CompanionApp, draft: GoalDraft, stored: com.accountabilibuddy.shared.goals.Goal?, onMessage: (String) -> Unit): String? {
    val lists = app.goals.lists.value
    val position = (lists.liveGoals.filter { it.areaId == draft.areaId }.maxOfOrNull { it.position } ?: 0.0) + 1.0
    val goal = GoalDrafts.toGoal(draft, stored, OffsetDateTime.now(), position) ?: return GoalDrafts.basicsProblem(draft) ?: GoalDrafts.targetProblem(draft) ?: GoalDrafts.sourcesProblem(draft) ?: "That goal can't be saved."
    val error = if (stored == null) app.goals.addGoal(goal) else app.goals.updateGoal(goal)
    if (error != null) return error
    if (stored != null && stored.isDeepWork) {
        val wanted = GoalDrafts.deepWorkWeeklyMinutes(draft)
        if (wanted != app.goals.deepWork.current().minutes) {
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
