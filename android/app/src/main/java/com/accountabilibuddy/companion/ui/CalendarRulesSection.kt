package com.accountabilibuddy.companion.ui

import android.Manifest
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.calendar.PhoneCalendar
import com.accountabilibuddy.companion.model.CalendarRule
import com.accountabilibuddy.companion.model.CalendarRuleMode
import com.accountabilibuddy.companion.model.CalendarRuleSet
import com.accountabilibuddy.companion.model.CategoryBehavior
import com.accountabilibuddy.companion.model.DayCategory
import com.accountabilibuddy.companion.model.KeywordCategory
import com.accountabilibuddy.companion.placement.GuardsLock
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbSegmented
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.kit.AbToggleRow
import com.accountabilibuddy.companion.ui.kit.AbValueButton
import com.accountabilibuddy.companion.ui.theme.Ab
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

private val TIME: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)
private val MODES = listOf(CalendarRuleMode.Off, CalendarRuleMode.Keywords, CalendarRuleMode.EveryEvent)
private val MODE_LABELS = listOf("Off", "Keywords", "Every event")

/**
 * Settings → Your day → "Calendar events in the day plan" (ADR 58): a switch (the calendar permission is asked for only when it is turned
 * on), and for each calendar of this phone Off, Keywords or Every event with the category the events get. Stored on this phone only.
 * A saved change waits 10 minutes and is refused while a focus session runs (changing a rule is a looser change); the page rests then.
 */
@Composable
internal fun CalendarRulesSection() {
    val app = LocalContext.current.applicationContext as CompanionApp
    val rules by app.calendarRules.rules.collectAsState()
    val state by app.store.state.collectAsState()
    val zone = ZoneId.systemDefault()
    val lock = remember(rules) { GuardsLock.at(app, Instant.now()) }
    val categories = state.document.dayPlan.categories.filter { it.behavior != CategoryBehavior.Sleep && it.behavior != CategoryBehavior.PreWork }
    var draft by remember(rules.active, rules.pending) { mutableStateOf(rules.pending ?: rules.active) }
    var refused by remember { mutableStateOf(false) }
    var granted by remember { mutableStateOf(app.calendarOverlay.hasPermission()) }
    var calendars by remember { mutableStateOf(emptyList<PhoneCalendar>()) }
    val ask = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { ok ->
        granted = ok
        if (ok) app.watchCalendar()
    }
    LaunchedEffect(granted) {
        calendars = if (granted) withContext(Dispatchers.IO) { app.calendarOverlay.calendars() } else emptyList()
    }

    val resting = lock != null
    val changed = draft != (rules.pending ?: rules.active)
    val summary = if (rules.active.enabled) "On" else "Off"
    Section("Calendar events in the day plan", summary = summary) {
        Note("Give calendar events a day plan category so the guards follow your real day. A stricter event applies at once; a looser one only if it was known 10 minutes before it starts, and never during a session. Changing these rules waits 10 minutes. What is read stays on this phone.")
        if (resting) Note(lock!!.sentence(zone))
        AbToggleRow(
            "Follow calendar events", draft.enabled,
            { on ->
                draft = draft.copy(enabled = on)
                if (on && !granted) ask.launch(Manifest.permission.READ_CALENDAR)
            },
            enabled = !resting,
        )
        if (draft.enabled && !granted) Note("Calendar access is off, so only events your PC sends count. Turn it on to use this phone's calendars.", color = Ab.colors.waiting)
        if (draft.enabled && granted) {
            if (calendars.isEmpty()) Note("No calendars found on this phone.")
            calendars.forEach { calendar -> CalendarRuleRow(calendar, draft, categories, !resting) { draft = it } }
            KeywordsEditor(draft, categories, !resting) { draft = it }
        }

        rules.pending?.let { Note("A change is waiting; it takes over at ${rules.pendingTakesOverAt?.atZone(zone)?.let(TIME::format) ?: "its time"}.") }
        if (refused) Note("Not saved: rules can't change during a session.", color = Ab.colors.waiting)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.CenterVertically) {
            AbPrimaryButton("Save (takes over in 10 min)", { refused = !app.calendarRules.trySave(draft, Instant.now(), lock != null) }, enabled = changed && !resting)
            if (rules.pending != null) AbTextButton("Cancel waiting change", { app.calendarRules.cancelPending() })
        }
    }
}

@Composable
private fun CalendarRuleRow(calendar: PhoneCalendar, draft: CalendarRuleSet, categories: List<DayCategory>, enabled: Boolean, onChange: (CalendarRuleSet) -> Unit) {
    val rule = draft.ruleFor(calendar.id) ?: CalendarRule(calendar.id, CalendarRuleMode.Off)
    fun put(next: CalendarRule) = onChange(draft.copy(rules = draft.rules.filter { it.calendarId != calendar.id } + next))
    Column(Modifier.padding(top = 6.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(calendar.name.ifBlank { "Calendar" }, style = Ab.type.bodyMedium, color = Ab.colors.ink)
        if (calendar.account.isNotBlank()) Note(calendar.account)
        if (enabled) {
            AbSegmented(MODE_LABELS, MODES.indexOf(rule.mode), { put(rule.copy(mode = MODES[it])) })
        } else {
            Note(MODE_LABELS[MODES.indexOf(rule.mode)])
        }
        // Indented under its calendar: the category every event of it gets.
        if (rule.mode == CalendarRuleMode.EveryEvent) {
            val chosen = categories.firstOrNull { it.id == rule.categoryId }
            Row(Modifier.padding(start = 16.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Note("Category")
                AbValueButton(chosen?.name ?: "Choose", { put(rule.copy(categoryId = next(categories, chosen)?.id)) }, enabled = enabled && categories.isNotEmpty())
            }
        }
        if (rule.mode == CalendarRuleMode.Keywords) Note("Uses the keyword list below.", Modifier.padding(start = 16.dp))
    }
}

@Composable
private fun KeywordsEditor(draft: CalendarRuleSet, categories: List<DayCategory>, enabled: Boolean, onChange: (CalendarRuleSet) -> Unit) {
    var word by remember { mutableStateOf("") }
    var category by remember(categories) { mutableStateOf(categories.firstOrNull()) }
    Column(Modifier.padding(top = 10.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text("Keywords", style = Ab.type.bodyMedium, color = Ab.colors.ink)
        Note("An event with a keyword in its title gets that category (for calendars set to Keywords).")
        draft.globalKeywords.forEach { entry ->
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("${entry.keyword} → ${categories.firstOrNull { it.id == entry.categoryId }?.name ?: "(gone)"}", style = Ab.type.body, color = Ab.colors.ink, modifier = Modifier.weight(1f))
                AbTextButton("Remove", { onChange(draft.copy(globalKeywords = draft.globalKeywords - entry)) }, enabled = enabled)
            }
        }
        if (enabled) {
            AbTextField(word, { word = it }, "Keyword", capitalization = KeyboardCapitalization.None)
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                AbValueButton(category?.name ?: "Category", { category = next(categories, category) }, enabled = categories.isNotEmpty())
                AbTextButton("Add", {
                    val chosen = category
                    if (word.isNotBlank() && chosen != null) {
                        onChange(draft.copy(globalKeywords = draft.globalKeywords + KeywordCategory(word.trim(), chosen.id)))
                        word = ""
                    }
                }, enabled = word.isNotBlank() && category != null)
            }
        }
    }
}

// The category after [current] in [list] (the first when there is none yet), for a button that cycles through them.
private fun next(list: List<DayCategory>, current: DayCategory?): DayCategory? {
    if (list.isEmpty()) return null
    val index = list.indexOfFirst { it.id == current?.id }
    return list[(index + 1) % list.size]
}
