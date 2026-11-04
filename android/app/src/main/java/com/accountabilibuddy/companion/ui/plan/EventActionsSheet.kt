package com.accountabilibuddy.companion.ui.plan

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.EventCategoryResolver
import com.accountabilibuddy.companion.engine.EventKeywordRules
import com.accountabilibuddy.companion.model.CalendarEvent
import com.accountabilibuddy.companion.model.DayCategory
import com.accountabilibuddy.companion.ui.kit.AbChip
import com.accountabilibuddy.companion.ui.kit.AbChipRow
import com.accountabilibuddy.companion.ui.kit.AbSheet
import com.accountabilibuddy.companion.ui.tasks.SheetField
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.format.DateTimeFormatter
import java.util.Locale

/** Hosts the event sheet: [content] gets what to call when an event is tapped. */
@Composable
fun EventActionsHost(content: @Composable (onEvent: (CalendarEvent) -> Unit) -> Unit) {
    var open by remember { mutableStateOf<CalendarEvent?>(null) }
    content { open = it }
    open?.let { event -> EventActionsSheet(event, onDismiss = { open = null }) }
}

/**
 * What can be done with a calendar event (ADR 64): what it counts as now, classify it (the whole series or just this one), Don't classify, Back to automatic, Always
 * classify like this, and Add a task for it. All-day events can only make a task, since they can't carry a category. The sheet stays open and says what happened.
 */
@Composable
fun EventActionsSheet(event: CalendarEvent, onDismiss: () -> Unit) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val actions = remember(app) { EventActions(app) }
    val colors = Ab.colors
    var wholeSeries by remember { mutableStateOf(true) }
    var message by remember { mutableStateOf<String?>(null) }
    var makingRule by remember { mutableStateOf(false) }
    val keyword = remember { mutableStateOf(EventKeywordRules.suggestKeyword(event.title)) }
    val usable = EventCategoryResolver.isUsable(event)
    val categories = remember { actions.categories() }
    val current = actions.currentCategory(event)

    AbSheet(onDismiss = onDismiss) {
        Column(Modifier.fillMaxWidth().verticalScroll(rememberScrollState())) {
            Column(Modifier.padding(horizontal = 16.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(event.title, style = Ab.type.bodyMedium, color = colors.ink)
                Text(whenText(event), style = Ab.type.small, color = colors.muted)
                if (usable) Text(current?.let { "Counts as ${it.name}" } ?: "The day plan decides", style = Ab.type.small, color = colors.muted)
            }

            if (usable) {
                if (actions.mayRepeat(event)) {
                    AbChipRow(Modifier.padding(horizontal = 16.dp, vertical = 8.dp)) {
                        AbChip("All in the series", selected = wholeSeries, onClick = { wholeSeries = true })
                        AbChip("Just this one", selected = !wholeSeries, onClick = { wholeSeries = false })
                    }
                }

                SectionLabel("Classify as")
                categories.forEach { category ->
                    CategoryRow(category, selected = current?.id == category.id) { message = actions.classify(event, category, wholeSeries) }
                }
                ActionRow("Don't classify") { message = actions.classify(event, null, wholeSeries) }
                ActionRow("Back to automatic") { message = actions.backToAutomatic(event, wholeSeries) }
                if (actions.canMakeRule(event)) {
                    ActionRow("Always classify like this…") { makingRule = !makingRule }
                }

                if (makingRule) {
                    SheetField(
                        value = keyword.value, onValueChange = { keyword.value = it }, placeholder = "Words in the title", singleLine = true, onDone = {},
                        modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp),
                    )
                    Text("Tap the category those events count as. A rule change waits before it applies.", style = Ab.type.small, color = colors.muted, modifier = Modifier.padding(horizontal = 16.dp))
                    AbChipRow(Modifier.padding(horizontal = 16.dp, vertical = 8.dp)) {
                        categories.forEach { category ->
                            AbChip(category.name, selected = false, onClick = {
                                message = actions.makeRule(event, keyword.value, category)
                                makingRule = false
                            })
                        }
                    }
                }
            }

            ActionRow("Add a task for it") { message = actions.addTask(event) }
            message?.let { Text(it, style = Ab.type.small, color = colors.ink, modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp)) }
        }
    }
}

@Composable
private fun SectionLabel(text: String) {
    Text(text, style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(start = 16.dp, end = 16.dp, top = 12.dp, bottom = 4.dp))
}

@Composable
private fun CategoryRow(category: DayCategory, selected: Boolean, onClick: () -> Unit) {
    Row(
        Modifier.fillMaxWidth().heightIn(min = 48.dp).clickable(onClick = onClick).padding(horizontal = 16.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Box(Modifier.size(12.dp).background(Color(category.argb.toInt()), CircleShape))
        Text(category.name, style = Ab.type.body, color = Ab.colors.ink, modifier = Modifier.weight(1f))
        if (selected) Text("✓", style = Ab.type.body, color = Ab.colors.ink)
    }
}

@Composable
private fun ActionRow(text: String, onClick: () -> Unit) {
    Box(Modifier.fillMaxWidth().heightIn(min = 48.dp).clickable(onClick = onClick).padding(horizontal = 16.dp), contentAlignment = Alignment.CenterStart) {
        Text(text, style = Ab.type.body, color = Ab.colors.ink)
    }
}

private val DAY: DateTimeFormatter = DateTimeFormatter.ofPattern("EEE MMM d", Locale.US)
private val TIME: DateTimeFormatter = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

private fun whenText(event: CalendarEvent): String {
    val day = event.localStart.format(DAY)
    return if (event.isAllDay) "$day · all day" else "$day · ${event.localStart.format(TIME)} to ${event.localEnd.format(TIME)}"
}
