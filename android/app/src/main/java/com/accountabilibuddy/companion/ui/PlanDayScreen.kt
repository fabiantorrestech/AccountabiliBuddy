package com.accountabilibuddy.companion.ui

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.DayPlanRequest
import com.accountabilibuddy.companion.engine.DayPlanner
import com.accountabilibuddy.companion.model.BlockLift
import com.accountabilibuddy.companion.model.BlockRef
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.SleepTimes
import java.time.Duration
import java.time.LocalDate
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import java.util.Locale

/** One block "Plan a day" can lift: the block, its device's name, whether it's on now. */
data class PlanDayBlock(val block: BlockRef, val device: String, val on: Boolean)

/**
 * "Plan a day" on the phone (ADR 37), the same four steps as the PC: kind of day and dates, the night before, blocks
 * to lift (window, Allow or Auto, re-enable time, heads-up), then a summary. [onSave] returns null when saved (it
 * goes to the PC like an Edit plan change), else why not.
 */
@Composable
fun PlanDayScreen(plan: DayPlan, blocks: List<PlanDayBlock>, onSave: (DayPlanRequest) -> String?, onClose: () -> Unit) {
    val today = remember { LocalDate.now() }
    var step by remember { mutableStateOf(1) }
    var offDay by remember { mutableStateOf(false) }
    val dates = remember { mutableStateListOf(today.plusDays(1)) }
    var note by remember { mutableStateOf("") }
    var error by remember { mutableStateOf<String?>(null) }

    fun template() = if (offDay) plan.otherDay else plan.workday
    fun nightOf(off: Boolean): SleepTimes? {
        val t = if (off) plan.otherDay else plan.workday
        return DayPlan(plan.categories, plan.workdays, t, t, emptyList()).nightBefore(today)
    }

    var bedtime by remember { mutableStateOf(nightOf(false)?.bedtime ?: LocalTime.of(23, 0)) }
    var wake by remember { mutableStateOf(nightOf(false)?.wake ?: LocalTime.of(7, 0)) }
    var byAlarm by remember { mutableStateOf(nightOf(false)?.wakeByAlarm ?: false) }
    val lifted = remember { mutableStateListOf<BlockRef>() }
    var liftFrom by remember { mutableStateOf(LocalTime.MIDNIGHT) }
    var liftTo by remember { mutableStateOf(plan.workday.rules.reenableAt) }
    var auto by remember { mutableStateOf(false) }
    var reenable by remember { mutableStateOf(plan.workday.rules.reenableAt) }
    var headsUp by remember { mutableStateOf((plan.workday.rules.warnMinutes ?: 0) > 0) }

    fun setKind(off: Boolean) {
        offDay = off
        nightOf(off)?.let { bedtime = it.bedtime; wake = it.wake; byAlarm = it.wakeByAlarm }
        val rules = template().rules
        reenable = rules.reenableAt
        liftTo = rules.reenableAt
        headsUp = (rules.warnMinutes ?: 0) > 0
    }

    fun request(): DayPlanRequest {
        val rules = template().rules
        val lifts = if (lifted.isEmpty()) rules.lifts else listOf(BlockLift(liftFrom, liftTo, if (auto) BlockLift.AUTO else BlockLift.ALLOW, lifted.toList()))
        return DayPlanRequest(
            dates.sorted(), offDay, SleepTimes(bedtime, wake, byAlarm),
            rules.copy(reenableAt = reenable, warnMinutes = if (headsUp) rules.warnMinutes ?: 5 else null, lifts = lifts),
            note,
        )
    }

    BackHandler { if (step > 1) step-- else onClose() }
    Column(Modifier.fillMaxSize().padding(16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("Plan a day", style = MaterialTheme.typography.headlineSmall, modifier = Modifier.weight(1f))
            TextButton(onClick = onClose) { Text("Cancel") }
        }
        Text(
            when (step) {
                1 -> "1 of 4 · What kind of day?"
                2 -> "2 of 4 · Sleep the night before"
                3 -> "3 of 4 · Blocks"
                else -> "4 of 4 · Check and save"
            },
            style = MaterialTheme.typography.titleMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Spacer(Modifier.padding(4.dp))
        Column(Modifier.weight(1f).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            when (step) {
                1 -> {
                    Choice("Adjusted workday (from your Workdays plan)", !offDay) { setKind(false) }
                    Choice("Off-day (from your Off-Days plan)", offDay) { setKind(true) }
                    Text("Dates", fontWeight = FontWeight.SemiBold)
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        (0L until 28L).map { today.plusDays(it) }.forEach { date ->
                            FilterChip(
                                selected = date in dates,
                                onClick = { if (date in dates) dates.remove(date) else dates.add(date) },
                                label = { Text(if (date == today) "Today" else dateText(date)) },
                            )
                        }
                    }
                    OutlinedTextField(note, { note = it }, label = { Text("Note (optional)") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
                }
                2 -> {
                    Text("The night before each date.", color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Labeled("Bed by") { TimeWheel(bedtime, { bedtime = it }) }
                    Labeled("Up at") { TimeWheel(wake, { wake = it }) }
                    Text(sleepText(bedtime, wake), fontWeight = FontWeight.SemiBold)
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("Follow my alarm that morning", modifier = Modifier.weight(1f))
                        Switch(byAlarm, { byAlarm = it })
                    }
                }
                3 -> {
                    if (blocks.isEmpty()) {
                        Text("No blocks seen yet: Curbox on this phone and Cold Turkey on your PCs show up once they've synced.",
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                    } else {
                        Text("Tick the blocks to lift on these days.", color = MaterialTheme.colorScheme.onSurfaceVariant)
                        blocks.forEach { item ->
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Checkbox(item.block in lifted, { on -> if (on) lifted.add(item.block) else lifted.remove(item.block) })
                                Text("${item.device} · ${item.block.name}${if (item.on) "" else " (off now)"}")
                            }
                        }
                    }
                    if (lifted.isNotEmpty()) {
                        Labeled("Lifted from") { TimeWheel(liftFrom, { liftFrom = it }) }
                        Labeled("until") { TimeWheel(liftTo, { liftTo = it }) }
                        Choice("Allow: I may turn them off myself then", !auto) { auto = false }
                        Choice("Auto: turn them off for me when it starts", auto) { auto = true }
                    }
                    HorizontalDivider()
                    Labeled("Blocks back on at") { TimeWheel(reenable, { reenable = it }) }
                    Text("12:00 AM = midnight, the end of the day. Any block you turned off comes back on then.",
                        style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("Heads-up 5 minutes before", modifier = Modifier.weight(1f))
                        Switch(headsUp, { headsUp = it })
                    }
                }
                else -> {
                    summary(plan, request(), lifted).forEach { Text(it) }
                    Text("Saving makes a one-off day for each date and sends it to your PC; Workdays and Off-Days stay as they are.",
                        style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
            error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth().padding(top = 8.dp)) {
            if (step > 1) OutlinedButton(onClick = { step-- }) { Text("Back") }
            Spacer(Modifier.weight(1f))
            if (step < 4) {
                Button(onClick = { step++ }, enabled = step != 1 || dates.isNotEmpty()) { Text("Next") }
            } else {
                Button(onClick = { error = onSave(request()); if (error == null) onClose() }) { Text("Save") }
            }
        }
    }
}

@Composable
private fun Choice(text: String, selected: Boolean, onClick: () -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        RadioButton(selected, onClick)
        Text(text)
    }
}

@Composable
private fun Labeled(label: String, content: @Composable () -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Text(label, modifier = Modifier.width(120.dp))
        content()
    }
}

private val dateFormat = DateTimeFormatter.ofPattern("EEE M/d", Locale.US)
private val timeFormat = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

private fun dateText(date: LocalDate) = date.format(dateFormat)

private fun timeText(time: LocalTime) = if (time == LocalTime.MIDNIGHT) "midnight" else time.format(timeFormat)

private fun sleepText(bed: LocalTime, wake: LocalTime): String {
    var length = Duration.between(bed, wake)
    if (!length.isPositive) length = length.plusDays(1)
    return "${length.toHours()} h ${"%02d".format(length.toMinutesPart())} m of sleep"
}

private fun summary(plan: DayPlan, request: DayPlanRequest, lifted: List<BlockRef>): List<String> {
    val lines = mutableListOf("${if (request.offDay) "Off-day" else "Adjusted workday"} on ${request.dates.joinToString(", ") { dateText(it) }}")
    request.sleep?.let {
        lines += "The night before: bed by ${timeText(it.bedtime)}, up at ${timeText(it.wake)}${if (it.wakeByAlarm) " (or your alarm)" else ""} · ${sleepText(it.bedtime, it.wake)}"
    }
    val rules = request.blocks!!
    if (lifted.isNotEmpty()) {
        val lift = rules.lifts.first()
        lines += "Lifted ${timeText(lift.start)}–${timeText(lift.end)} (${if (lift.mode == BlockLift.AUTO) "turned off for you" else "you may turn them off"}): ${lifted.joinToString(", ") { it.name }}"
    }
    lines += "Blocks you turned off come back on at ${timeText(rules.reenableAt)}${rules.warnMinutes?.let { ", with a heads-up $it min before" } ?: ""}"
    DayPlanner.clashes(plan, request.dates).takeIf { it.isNotEmpty() }?.let { clash ->
        lines += "Replaces the plan already set for ${clash.joinToString(", ") { dateText(it) }}"
    }
    return lines
}
