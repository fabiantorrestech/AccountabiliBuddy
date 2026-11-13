package com.accountabilibuddy.companion.ui.tasks.page

import android.app.DatePickerDialog
import android.app.TimePickerDialog
import androidx.compose.runtime.withFrameNanos
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.semantics.selected
import com.accountabilibuddy.shared.tasks.ShelfEdits
import java.time.LocalDate
import java.time.LocalTime
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.TextRange
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.OffsetMapping
import androidx.compose.ui.text.input.TextFieldValue
import androidx.compose.ui.text.input.TransformedText
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbSwitch
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.tasks.SheetField
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.sync.TaskLabels
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskBins
import com.accountabilibuddy.shared.tasks.TaskLabel
import com.accountabilibuddy.shared.tasks.TaskRowText
import com.accountabilibuddy.shared.tasks.TaskTitleHelp

/** What the Add sheet hands over for a new task: the title without its "@label" and "#tag" words, where it goes, and the rest. */
class AddTaskDraft(
    val title: String,
    val bin: TaskBin,
    val labelId: String?,
    val tags: List<String>,
    val estimateMinutes: Int?,
    val note: String?,
    val steps: List<String>,
    val dueOn: LocalDate? = null,
    val dueAt: LocalTime? = null,
    val reminders: List<String> = emptyList(),
)

/** The time limits the sheets offer (minutes). */
val TIME_LIMIT_CHOICES: List<Int> = listOf(10, 15, 25, 50, 90)

/**
 * "Add a task" (board Tasks-Phone-Add; brief §4.5): the title "Add a task" with the subtitle naming where it goes ("Shelf · Next"),
 * the field with an accent border that reads **@label** and **#tag** as they are typed (tinted in the field, and shown as removable
 * chips below: a label's chip with its dot), then "+ Time limit", "+ Reminder", "+ Steps" and "+ Note", the **Send to** grid
 * (Today: This session, Queued; Shelf: To sort, Next, Later) that starts on the bin you are looking at ([startBin]), and the footer
 * with the **Add another** switch (keeps the sheet open and clears the field) and **Add**.
 *
 * While "@" or "#" is being typed, the labels and tags that match are offered as chips (a label whose name has a space can only be
 * picked that way). The title's counter shows from 50 characters ("52 / 60"; nothing stops a longer one). A page's own label or tag
 * ([startLabel], [startTag]) starts as a chip. "+ Reminder" sets the due date (Today, Tomorrow or a picked day; an optional time)
 * and its reminders (At the time, 10 min before, 1 hr before, The day before at 9:00 AM; a date with no time stands for 9:00 AM, as on
 * the PC). They ring on the devices the task's "Rings on" lists name; this phone rings for task reminders once it has them (R7.4).
 *
 * It opens at its full height with the field focused and the keyboard up at once; the keyboard makes room inside the sheet, so
 * the sheet itself never moves ([AbSheet] `aboveKeyboard`).
 *
 * @param onAdd Adds the task; true when it was added.
 * @param another Whether "Add another" is on (kept by the page while Tasks is open).
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun AddTaskSheet(
    startBin: TaskBin,
    startLabel: String?,
    startTag: String?,
    labels: List<TaskLabel>,
    knownTags: List<String>,
    another: Boolean,
    onAnother: (Boolean) -> Unit,
    onAdd: (AddTaskDraft) -> Boolean,
    onDismiss: () -> Unit,
) {
    val colors = Ab.colors
    var text by remember { mutableStateOf(TextFieldValue("")) }
    var bin by remember { mutableStateOf(startBin) }
    var pickedLabel by remember { mutableStateOf(startLabel) }
    var extraTags by remember { mutableStateOf(listOfNotNull(startTag)) }
    var limit by remember { mutableStateOf<Int?>(null) }
    var limitOpen by remember { mutableStateOf(false) }
    var stepsOpen by remember { mutableStateOf(false) }
    var steps by remember { mutableStateOf(listOf<String>()) }
    var step by remember { mutableStateOf("") }
    var noteOpen by remember { mutableStateOf(false) }
    var note by remember { mutableStateOf("") }
    var reminderOpen by remember { mutableStateOf(false) }
    var dueOn by remember { mutableStateOf<LocalDate?>(null) }
    var dueAt by remember { mutableStateOf<LocalTime?>(null) }
    var rules by remember { mutableStateOf(listOf<String>()) }
    val focus = remember { FocusRequester() }
    val keyboard = LocalSoftwareKeyboardController.current
    val context = LocalContext.current
    val today = LocalDate.now()
    // The field takes the keyboard as soon as the sheet is drawn (its first frame), so the keyboard comes up with the sheet.
    LaunchedEffect(Unit) {
        withFrameNanos { }
        runCatching { focus.requestFocus() }
        keyboard?.show()
    }

    val typed = TaskTitleHelp.parse(text.text, labels)
    val typedLabel = typed.labelId
    val labelId = typedLabel ?: pickedLabel
    val label = labels.firstOrNull { it.id.equals(labelId, ignoreCase = true) }
    val tags = TaskLabels.clean(typed.tags + extraTags).orEmpty()
    val setText = { value: String -> text = TextFieldValue(value, TextRange(value.length)) }

    val add = {
        val allSteps = steps + listOfNotNull(step.trim().takeIf { it.isNotEmpty() })
        val draft = AddTaskDraft(typed.title, bin, labelId, tags, limit, note.trim().takeIf { it.isNotEmpty() }, allSteps, dueOn, dueAt, if (dueOn != null) rules else emptyList())
        if (typed.title.isNotBlank() && onAdd(draft)) {
            if (another) {
                // The field clears for the next one; where it goes, its label and its tags stay as they were picked.
                setText("")
                steps = emptyList()
                step = ""
                note = ""
                dueOn = null
                dueAt = null
                rules = emptyList()
                reminderOpen = false
                runCatching { focus.requestFocus() }
            } else {
                onDismiss()
            }
        }
    }

    AbSheet(
        title = "Add a task",
        subtitle = TaskBins.place(bin),
        onDismiss = onDismiss,
        height = AbSheetHeight.Tall,
        aboveKeyboard = true,
        footer = {
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Row(
                    Modifier.weight(1f).heightIn(min = 48.dp).clip(RoundedCornerShape(14.dp)).clickable(role = Role.Switch) { onAnother(!another) },
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    AbSwitch(another)
                    Text("Add another", style = Ab.type.small.copy(fontSize = 13.5.sp), color = colors.muted)
                }
                AbPrimaryButton("Add", onClick = add, enabled = typed.title.isNotBlank())
            }
        },
    ) {
        // The field: an accent border; the "@label" and "#tag" words it reads are tinted as they are typed.
        val marks = TaskTitleHelp.marks(text.text, labels)
        val tintMarks = VisualTransformation { plain -> TransformedText(tinted(plain.text, marks, colors.accent), OffsetMapping.Identity) }
        Box(
            Modifier.padding(horizontal = 24.dp).fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(colors.surface2)
                .border(1.5.dp, colors.accent, RoundedCornerShape(16.dp)).padding(horizontal = 14.dp, vertical = 12.dp),
        ) {
            if (text.text.isEmpty()) Text("What needs doing?", style = Ab.type.body.copy(fontSize = 16.sp), color = colors.faint)
            BasicTextField(
                value = text,
                onValueChange = { text = it },
                modifier = Modifier.fillMaxWidth().focusRequester(focus).semantics { contentDescription = "Task title" },
                textStyle = Ab.type.body.copy(fontSize = 16.sp, lineHeight = 22.sp, color = colors.ink),
                cursorBrush = SolidColor(colors.accent),
                visualTransformation = tintMarks,
                keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Sentences, imeAction = ImeAction.Done),
                keyboardActions = KeyboardActions(onDone = { add() }),
                maxLines = 4,
            )
        }
        Row(Modifier.fillMaxWidth().padding(start = 24.dp, end = 24.dp, top = 6.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Text("Type @ for a label or # for a tag", style = Ab.type.small.copy(fontSize = 12.5.sp), color = colors.faint, modifier = Modifier.weight(1f))
            TaskTitleHelp.counter(typed.title)?.let { counter ->
                Text(counter, style = Ab.type.small.copy(fontSize = 12.5.sp, fontFeatureSettings = "tnum"), color = if (TaskTitleHelp.isLong(typed.title)) colors.amberInk else colors.faint)
            }
        }

        // What is being typed after "@" or "#": the labels and tags that match, to pick with a tap.
        val word = text.text.take(text.selection.end.coerceIn(0, text.text.length)).takeLastWhile { !it.isWhitespace() }
        val wordStart = text.selection.end.coerceIn(0, text.text.length) - word.length
        val offered: List<Pair<String, () -> Unit>> = when {
            word.startsWith("@") -> labels.filter { it.name.startsWith(word.drop(1), ignoreCase = true) && !it.id.equals(typedLabel, ignoreCase = true) }.take(6).map { match ->
                match.name to {
                    // The typed "@…" goes and the label is picked (so a name with a space works too).
                    setText((text.text.removeRange(wordStart, wordStart + word.length)).trimEnd().let { if (it.isEmpty()) it else "$it " })
                    pickedLabel = match.id
                }
            }

            word.startsWith("#") && word.length > 1 -> knownTags.filter { it.startsWith(word.drop(1), ignoreCase = true) && !it.equals(word.drop(1), ignoreCase = true) }.take(6).map { match ->
                "#$match" to { setText(text.text.replaceRange(wordStart, wordStart + word.length, "#$match") + " ") }
            }

            else -> emptyList()
        }
        if (offered.isNotEmpty()) {
            FlowRow(Modifier.padding(start = 24.dp, end = 24.dp, top = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                offered.forEach { (name, pick) -> PlusChip(name, onClick = pick) }
            }
        }

        FlowRow(
            Modifier.padding(start = 24.dp, end = 24.dp, top = 12.dp, bottom = 4.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            if (label != null) {
                SetChip(label.name, remove = "Remove the label ${label.name}", dot = label.color) {
                    if (typedLabel != null) setText(TaskTitleHelp.without(text.text, "@" + label.name)) else pickedLabel = null
                }
            }
            tags.forEach { tag ->
                SetChip("#$tag", remove = "Remove the tag $tag") {
                    if (TaskLabels.has(typed.tags, tag)) setText(TaskTitleHelp.without(text.text, "#$tag"))
                    extraTags = extraTags.filterNot { TaskLabels.same(it, tag) }
                }
            }
            val chosen = limit
            if (chosen != null) {
                SetChip(TaskRowText.minutes(chosen), remove = "Remove the time limit") { limit = null }
            } else {
                PlusChip("+ Time limit", onClick = { limitOpen = !limitOpen })
            }
            val due = dueOn
            if (due != null) {
                val words = listOf(TaskRowText.due(due, dueAt, today)) + rules.take(1).map { ShelfEdits.reminderWords(it) } + (if (rules.size > 1) listOf("+${rules.size - 1}") else emptyList())
                SetChip(words.joinToString(" · "), remove = "Remove the due date and its reminders") {
                    dueOn = null
                    dueAt = null
                    rules = emptyList()
                    reminderOpen = false
                }
                if (!reminderOpen) PlusChip("Change", onClick = { reminderOpen = true })
            } else {
                PlusChip("+ Reminder", onClick = { reminderOpen = !reminderOpen })
            }
            if (!stepsOpen) PlusChip("+ Steps", onClick = { stepsOpen = true })
            if (!noteOpen) PlusChip("+ Note", onClick = { noteOpen = true })
        }
        if (limitOpen && limit == null) {
            FlowRow(Modifier.padding(horizontal = 24.dp, vertical = 4.dp), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                TIME_LIMIT_CHOICES.forEach { minutes -> PlusChip(TaskRowText.minutes(minutes), onClick = { limit = minutes; limitOpen = false }) }
            }
        }
        if (reminderOpen) {
            Column(Modifier.padding(horizontal = 24.dp, vertical = 4.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text("Due", style = Ab.type.small.copy(fontSize = 12.5.sp), color = colors.muted)
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    PickChip("Today", dueOn == today) { dueOn = today }
                    PickChip("Tomorrow", dueOn == today.plusDays(1)) { dueOn = today.plusDays(1) }
                    val other = dueOn?.takeIf { it != today && it != today.plusDays(1) }
                    PickChip(other?.let { TaskRowText.due(it, null, today) } ?: "Pick a day…", other != null) {
                        val start = dueOn ?: today
                        DatePickerDialog(context, { _, year, month, day -> dueOn = LocalDate.of(year, month + 1, day) }, start.year, start.monthValue - 1, start.dayOfMonth).show()
                    }
                }
                if (dueOn != null) {
                    Text("Time", style = Ab.type.small.copy(fontSize = 12.5.sp), color = colors.muted)
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        PickChip("No time", dueAt == null) { dueAt = null }
                        // 12-hour with AM/PM, whatever the phone's 24-hour setting.
                        PickChip(dueAt?.let { TaskRowText.time(it) } ?: "Pick a time…", dueAt != null) {
                            val start = dueAt ?: LocalTime.of(9, 0)
                            TimePickerDialog(context, { _, hour, minute -> dueAt = LocalTime.of(hour, minute) }, start.hour, start.minute, false).show()
                        }
                    }
                    Text("Remind me", style = Ab.type.small.copy(fontSize = 12.5.sp), color = colors.muted)
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        ShelfEdits.REMINDER_RULES.forEach { rule ->
                            PickChip(ShelfEdits.reminderWords(rule), rule in rules) { rules = if (rule in rules) rules - rule else rules + rule }
                        }
                    }
                    Text(
                        (if (dueAt == null) "A day with no time counts from 9:00 AM. " else "") + "Reminders ring on the devices your Rings on lists name.",
                        style = Ab.type.small.copy(fontSize = 12.sp), color = colors.faint,
                    )
                    PlusChip("Done", onClick = { reminderOpen = false })
                }
            }
        }
        if (stepsOpen) {
            Column(Modifier.padding(horizontal = 24.dp, vertical = 4.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text("Steps", style = Ab.type.small.copy(fontSize = 12.5.sp), color = colors.muted)
                steps.forEachIndexed { index, title ->
                    SetChip(title, remove = "Remove the step $title") { steps = steps.filterIndexed { at, _ -> at != index } }
                }
                val addStep = {
                    if (step.isNotBlank()) {
                        steps = steps + step.trim()
                        step = ""
                    }
                }
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    AbTextField(step, { step = it }, "Add a step", Modifier.weight(1f), keyboardType = KeyboardType.Text)
                    AbPrimaryButton("Add step", onClick = addStep, enabled = step.isNotBlank())
                }
            }
        }
        if (noteOpen) {
            Column(Modifier.padding(horizontal = 24.dp, vertical = 4.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text("Note", style = Ab.type.small.copy(fontSize = 12.5.sp), color = colors.muted)
                SheetField(value = note, onValueChange = { note = it }, placeholder = "A note for this task", onDone = {}, minLines = 2)
            }
        }

        MoveToGrid("Send to", bin, onPick = { bin = it }, modifier = Modifier.padding(top = 8.dp))
        Text("Starts on the bin you are looking at", style = Ab.type.small.copy(fontSize = 12.sp), color = colors.faint, modifier = Modifier.padding(horizontal = 24.dp))
    }
}

// The field's text with its "@label" and "#tag" words in the accent.
private fun tinted(text: String, marks: List<IntRange>, accent: Color): AnnotatedString = buildAnnotatedString {
    append(text)
    marks.forEach { range -> if (range.first >= 0 && range.last < text.length) addStyle(SpanStyle(color = accent), range.first, range.last + 1) }
}

// A chip for something that is set (a label with its dot, a tag, the time limit, a step): accent outline and tint, × takes it off.
@Composable
private fun SetChip(text: String, remove: String, dot: String? = null, onRemove: () -> Unit) {
    val colors = Ab.colors
    Row(
        Modifier.heightIn(min = 34.dp).clip(CircleShape).background(colors.accent.copy(alpha = 0.12f)).border(1.dp, colors.accent, CircleShape)
            .clickable(role = Role.Button, onClickLabel = remove, onClick = onRemove).padding(horizontal = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        if (dot != null) LabelDot(dot, 8.dp)
        Text(text, style = Ab.type.small.copy(fontSize = 13.5.sp), color = colors.ink, maxLines = 1)
        Text("×", style = Ab.type.small.copy(fontSize = 13.sp), color = colors.muted, modifier = Modifier.clearAndSetSemantics { })
    }
}

// A quiet chip that adds or picks something ("+ Time limit", a suggested label); [onClick] null draws it as coming soon.
@Composable
private fun PlusChip(text: String, onClick: (() -> Unit)?) {
    val colors = Ab.colors
    Box(
        Modifier.heightIn(min = 34.dp).clip(CircleShape).border(1.dp, colors.line, CircleShape)
            .then(if (onClick != null) Modifier.clickable(role = Role.Button, onClick = onClick) else Modifier.alpha(0.4f).semantics { contentDescription = "$text, coming soon" })
            .padding(horizontal = 12.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(text, style = Ab.type.small.copy(fontSize = 13.5.sp), fontWeight = FontWeight.Normal, color = colors.muted, maxLines = 1)
    }
}

// A choice among several ("Today", "10 min before"): tinted with the accent and outlined when picked.
@Composable
private fun PickChip(text: String, picked: Boolean, onClick: () -> Unit) {
    val colors = Ab.colors
    Box(
        Modifier.heightIn(min = 34.dp).clip(CircleShape)
            .then(if (picked) Modifier.background(colors.accent.copy(alpha = 0.12f)).border(1.dp, colors.accent, CircleShape) else Modifier.border(1.dp, colors.line, CircleShape))
            .clickable(role = Role.RadioButton, onClick = onClick)
            .semantics { selected = picked }
            .padding(horizontal = 12.dp),
        contentAlignment = Alignment.Center,
    ) {
        Text(text, style = Ab.type.small.copy(fontSize = 13.5.sp), color = if (picked) colors.ink else colors.muted, maxLines = 1)
    }
}
