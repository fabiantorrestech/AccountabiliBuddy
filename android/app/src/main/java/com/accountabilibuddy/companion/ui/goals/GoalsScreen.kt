package com.accountabilibuddy.companion.ui.goals

import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.material3.Surface
import androidx.compose.material3.Icon
import androidx.compose.material.icons.outlined.Add
import androidx.compose.material.icons.Icons
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.Box
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.core.tween
import androidx.compose.animation.expandVertically
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.shrinkVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.goals.GoalDisplayPrefs
import com.accountabilibuddy.companion.goals.GoalListView
import com.accountabilibuddy.companion.goals.GoalSettingsPrefs
import com.accountabilibuddy.companion.goals.PhoneGoalReview
import com.accountabilibuddy.companion.ui.LocalGuardsLock
import com.accountabilibuddy.companion.goals.PhoneGoalData
import com.accountabilibuddy.companion.goals.PhoneGoalStatus
import com.accountabilibuddy.companion.goals.PhoneGoalsOverview
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.companion.ui.LocalMotionScale
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbSegmentedCompact
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.motionSpec
import com.accountabilibuddy.companion.ui.theme.Ab
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import java.time.Instant
import java.time.ZoneId
import java.util.UUID

/**
 * The phone's Goals tab (round 2 R6a; brief §4.3, change note §4, boards Goals-Phone / -Options): the header with the week's counts,
 * the Stats pill and Options; the area chips; "YOUR GOALS n" with By area / Current / All; and the list, area by area in soft blocks
 * (folded areas remembered on this phone), each goal with its target, its one number and its kind's visual; and, when it is due, the
 * weekly review's card above "YOUR GOALS" with its sheet (round 2 R6b3).
 *
 * Every number comes from the shared goals maths ([PhoneGoalsOverview], worked out off the main thread and again each minute and
 * whenever the goals, the received files or the steps change). Goal and area names are drawn here and nowhere else (never logged).
 * Goals that can't be read show a note and nothing else; nothing is rebuilt on a guess.
 *
 * @param allReceived Every received device, removed devices' kept copies included (focus, as Stats counts it).
 * @param received The devices still in the group (Deep work's daily target: the newest PC's daily goal, read only).
 */
@Composable
fun GoalsScreen(allReceived: List<ReceivedDevice>, received: List<ReceivedDevice>, actions: GoalsActions) {
    val context = LocalContext.current
    val app = context.applicationContext as CompanionApp
    val status by app.goals.status.collectAsStateWithLifecycle()
    val lists by app.goals.lists.collectAsStateWithLifecycle()
    val display by GoalDisplayPrefs.display.collectAsStateWithLifecycle()
    val steps by app.steps.state.collectAsStateWithLifecycle()
    val weeklyState by app.goals.deepWork.state.collectAsStateWithLifecycle()
    var areaFilter by rememberSaveable { mutableStateOf<String?>(null) }
    var optionsOpen by rememberSaveable { mutableStateOf(false) }
    // A hold on a row, or its ⋮: the goal menu (the same rows and order as the PC's).
    var menuGoal by rememberSaveable { mutableStateOf<String?>(null) }
    // New goal (steps 1 and 2), Edit goal from the menu, Edit area (a goal sheet's own "+ Area" opens one over it), and Options › Areas.
    var newGoalOpen by rememberSaveable { mutableStateOf(false) }
    var editGoal by rememberSaveable { mutableStateOf<String?>(null) }
    var editAtReminders by rememberSaveable { mutableStateOf(false) }
    // The weekly review (round 2 R6b3): its sheet, and "Adjust the target" opening Edit goal › Target with the suggested numbers.
    var reviewOpen by rememberSaveable { mutableStateOf(false) }
    var adjusting by remember { mutableStateOf<GoalReviewAdjusting?>(null) }
    var areaSheet by rememberSaveable { mutableStateOf<String?>(null) }
    var areasOpen by rememberSaveable { mutableStateOf(false) }

    val reviewSettings by GoalSettingsPrefs.review.collectAsStateWithLifecycle()
    val guardsLock = LocalGuardsLock.current
    // A new minute (today's numbers, a day change) recounts.
    val minute by produceState(Instant.now()) {
        while (true) {
            val now = Instant.now()
            value = now
            delay(60_000L - (now.toEpochMilli() % 60_000L))
        }
    }
    val overview by produceState<PhoneGoalsOverview?>(null, status, lists, weeklyState, display.weekStart, allReceived, received, steps, minute) {
        if (status != PhoneGoalStatus.Ready) {
            value = null
            return@produceState
        }
        value = withContext(Dispatchers.Default) {
            runCatching {
                PhoneGoalsOverview.build(lists, display, allReceived.map { it.file }, received.map { it.file }, steps, Instant.now(), ZoneId.systemDefault(), app.goals.deepWork.current(), PhoneGoalData.slept(app))
            }.getOrNull()
        }
    }
    // The weekly review's card (round 2 R6b3): due from Settings › Goals' day and time, never during a session; worked out with the
    // trusted time ([PhoneGoalReview]) whenever the goals, the settings, the session or the minute change.
    val reviewMoment by produceState<PhoneGoalReview.Moment?>(null, status, lists, overview, reviewSettings, display.weekStart, guardsLock, minute) {
        val goals = overview?.facts?.map { it.goal }
        value = if (status != PhoneGoalStatus.Ready || goals == null) null else withContext(Dispatchers.Default) {
            runCatching { PhoneGoalReview.moment(app, goals) }.getOrNull()
        }
    }
    // An area chip whose area went away falls back to All.
    val selectedArea = areaFilter?.let { id -> overview?.areas?.firstOrNull { it.id.toString() == id }?.id }
    LaunchedEffect(overview?.areas, areaFilter) { if (areaFilter != null && overview != null && selectedArea == null) areaFilter = null }

    val subtitle = when (status) {
        PhoneGoalStatus.Ready -> overview?.summary?.subtitle() ?: " "
        PhoneGoalStatus.NotSetUp -> "Setting up your goals"
        PhoneGoalStatus.Unreadable -> "Your goals can't be read"
    }

    Box(Modifier.fillMaxSize()) {
        LazyColumn(
            Modifier.fillMaxSize(),
            // Room under the last block for the floating +.
            contentPadding = PaddingValues(bottom = 96.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            item(key = "header") {
                GoalsHeader(subtitle, onStats = actions.onOpenStats, onOptions = { optionsOpen = true }, modifier = Modifier.padding(horizontal = 16.dp))
            }
            if (status != PhoneGoalStatus.Ready) {
                item(key = "note") {
                    AbCard(Modifier.padding(horizontal = 16.dp)) {
                        Text(
                            if (status == PhoneGoalStatus.Unreadable) {
                                "Your goals on this phone can't be read (for example after the app's data was moved), so they are left as they are and nothing is changed. Stats still works."
                            } else {
                                "Your goals are being set up. Open the app again in a moment."
                            },
                            style = Ab.type.small, color = Ab.colors.muted,
                        )
                    }
                }
                return@LazyColumn
            }
            val shown = overview ?: return@LazyColumn
            val layout = GoalListLayout.of(shown, display, selectedArea)

            item(key = "chips") {
                GoalAreaChips(shown.areas, selectedArea, onSelect = { areaFilter = it?.toString() }, onNewGoal = { newGoalOpen = true })
            }
            reviewMoment?.takeIf { it.row.shown }?.let { moment ->
                item(key = "review") {
                    GoalReviewCard(
                        moment, reviewSettings.snoozeDays, onStart = { reviewOpen = true },
                        onSnooze = { choice -> PhoneGoalReview.snooze(app, choice)?.let(actions.onMessage) },
                        modifier = Modifier.padding(horizontal = 16.dp),
                    )
                }
            }
            item(key = "your-goals") {
                Row(Modifier.fillMaxWidth().padding(horizontal = 20.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text("YOUR GOALS", style = Ab.type.micro, fontWeight = FontWeight.SemiBold, color = Ab.colors.muted)
                    Text("  ${layout.count}", style = Ab.type.micro.copy(fontFeatureSettings = "tnum"), color = Ab.colors.faint, modifier = Modifier.weight(1f))
                    val views = GoalListView.entries
                    AbSegmentedCompact(views.map { it.label }, views.indexOf(display.view), { GoalDisplayPrefs.setView(context, views[it]) })
                }
            }
            items(layout.sections, key = { it.area?.id?.toString() ?: "all" }) { section ->
                GoalBlock(
                    section = section,
                    noPcGoal = shown.pcDailyMinutes == null,
                    onToggle = { area -> GoalDisplayPrefs.setCollapsed(context, area, !section.collapsed) },
                    actions = actions,
                    onMenu = { id -> menuGoal = id.toString() },
                    onEditArea = { id -> areaSheet = id.toString() },
                    openMenu = menuGoal,
                )
            }
            if (layout.pausedHidden > 0) {
                item(key = "paused-link") {
                    val word = if (layout.pausedHidden == 1) "1 paused goal" else "${layout.pausedHidden} paused goals"
                    AbTextButton("$word · show them in All", onClick = { GoalDisplayPrefs.setView(context, GoalListView.All) }, modifier = Modifier.padding(horizontal = 8.dp))
                }
            }
        }
        // The floating + (board Goals-Phone): a new goal, the same as the + chip, within the thumb's reach.
        if (status == PhoneGoalStatus.Ready) {
            Surface(
                onClick = { newGoalOpen = true },
                shape = Ab.shapes.mediumShape,
                color = Ab.colors.accent,
                contentColor = Ab.colors.onAccent,
                shadowElevation = 6.dp,
                modifier = Modifier.align(Alignment.BottomEnd).padding(end = 16.dp, bottom = 20.dp).size(60.dp).semantics { contentDescription = "New goal" },
            ) {
                Box(contentAlignment = Alignment.Center) { Icon(Icons.Outlined.Add, contentDescription = null, modifier = Modifier.size(28.dp)) }
            }
        }
    }

    // The review first, so Edit goal (its Adjust) opens over it.
    val reviewOverview = overview
    if (reviewOpen && reviewOverview != null) {
        GoalReviewSheet(
            reviewOverview, onDismiss = { reviewOpen = false },
            onAdjust = { id, adjustment -> adjusting = GoalReviewAdjusting(id, adjustment); editAtReminders = false; editGoal = id.toString() },
            onMessage = actions.onMessage,
        )
    }
    menuGoal?.let { raw ->
        val id = runCatching { UUID.fromString(raw) }.getOrNull()
        if (id == null) {
            menuGoal = null
        } else {
            GoalMenuSheet(
                id, allReceived, received, actions.onMessage, onDismiss = { menuGoal = null }, onEdit = { menuGoal = null; editAtReminders = false; editGoal = id.toString() },
                start = actions.session, onReminders = { menuGoal = null; editAtReminders = true; editGoal = id.toString() },
            )
        }
    }
    if (newGoalOpen) {
        GoalEditorSheets(goalId = null, allReceived = allReceived, received = received, onMessage = actions.onMessage, onDismiss = { newGoalOpen = false })
    }
    editGoal?.let { raw ->
        val id = runCatching { UUID.fromString(raw) }.getOrNull()
        if (id == null) {
            editGoal = null
        } else {
            GoalEditorSheets(
                goalId = id, allReceived = allReceived, received = received, onMessage = actions.onMessage,
                onDismiss = { editGoal = null; editAtReminders = false; adjusting = null }, startAtReminders = editAtReminders,
                startAtTarget = adjusting != null, suggested = adjusting?.takeIf { it.goalId == id }?.adjustment,
            )
        }
    }
    areaSheet?.let { raw ->
        // "new" makes an area; anything else is an area's id.
        GoalAreaSheet(
            areaId = runCatching { UUID.fromString(raw) }.getOrNull(), onDismiss = { areaSheet = null }, onMessage = actions.onMessage,
            // ⋮ on a goal in Edit area: this sheet closes and that goal's menu opens.
            onGoalMenu = { id -> areaSheet = null; menuGoal = id.toString() },
        )
    }
    if (areasOpen) {
        GoalAreasSheet(
            onDismiss = { areasOpen = false },
            onEdit = { id -> areaSheet = id.toString() },
            onNew = { areaSheet = "new" },
            onMessage = actions.onMessage,
        )
    }
    if (optionsOpen) {
        GoalsOptionsSheet(
            display = display,
            areaCount = overview?.areas?.size ?: 0,
            onView = { GoalDisplayPrefs.setView(context, it) },
            onWeekStart = { GoalDisplayPrefs.setWeekStart(context, it) },
            onHeatmapLabels = { GoalDisplayPrefs.setHeatmapLabels(context, it) },
            onExtraTiles = { GoalDisplayPrefs.setExtraTiles(context, it) },
            onOpenAreas = { optionsOpen = false; areasOpen = true },
            onOpenGoalsSettings = { optionsOpen = false; actions.onOpenGoalsSettings() },
            onDismiss = { optionsOpen = false },
        )
    }
}

// One soft block (brief §3.5: no dividers, rows apart by spacing): an area's header and its goals, or the one block of Current / All.
@Composable
private fun GoalBlock(section: GoalListSection, noPcGoal: Boolean, onToggle: (UUID) -> Unit, actions: GoalsActions, onMenu: (UUID) -> Unit, onEditArea: (UUID) -> Unit, openMenu: String?) {
    val scale = LocalMotionScale.current
    val area = section.area
    Column(
        // The theme's own card radius (28 Night Desk, 14 Instrument, 18 Paper), not a fixed one.
        Modifier.fillMaxWidth().padding(horizontal = 16.dp).clip(Ab.shapes.cardShape).background(Ab.colors.surface).padding(vertical = 4.dp),
    ) {
        if (area != null) {
            GoalAreaHeader(area, section.rows.size, section.collapsed, onToggle = { onToggle(area.id) }, onEdit = { onEditArea(area.id) })
        }
        // Folding moves the blocks under it, so it eases (150-250 ms); with motion off it only fades (brief §3.12).
        val reduced = scale <= 0f
        AnimatedVisibility(
            visible = !section.collapsed,
            enter = if (reduced) fadeIn(tween(150)) else fadeIn(motionSpec(scale, 200)) + expandVertically(motionSpec(scale, 200)),
            exit = if (reduced) fadeOut(tween(150)) else fadeOut(motionSpec(scale, 150)) + shrinkVertically(motionSpec(scale, 200)),
        ) {
            Column {
                if (section.rows.isEmpty()) {
                    Text(
                        if (area != null) "No goals here yet" else "No goals to show",
                        style = Ab.type.small, color = Ab.colors.faint, modifier = Modifier.padding(horizontal = 20.dp, vertical = 12.dp),
                    )
                }
                section.rows.forEach { facts ->
                    GoalRow(
                        facts = facts,
                        noPcGoal = noPcGoal,
                        onOpen = { actions.onOpenGoal(facts.goal.id) },
                        onMenu = { onMenu(facts.goal.id) },
                        menuOpen = openMenu == facts.goal.id.toString(),
                    )
                }
            }
        }
    }
}
