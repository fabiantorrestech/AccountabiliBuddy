package com.accountabilibuddy.companion.ui

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Bedtime
import androidx.compose.material.icons.outlined.Block
import androidx.compose.material.icons.outlined.CalendarMonth
import androidx.compose.material.icons.outlined.Computer
import androidx.compose.material.icons.outlined.KeyboardArrowDown
import androidx.compose.material.icons.outlined.KeyboardArrowUp
import androidx.compose.material.icons.outlined.Refresh
import androidx.compose.material.icons.outlined.Smartphone
import androidx.compose.material.icons.outlined.WarningAmber
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.AssistChip
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
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
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.engine.DayPlanRequest
import com.accountabilibuddy.companion.engine.DayPlanner
import com.accountabilibuddy.companion.model.BlockLift
import com.accountabilibuddy.companion.model.BlockRef
import com.accountabilibuddy.companion.model.BlockReenable
import com.accountabilibuddy.companion.model.DayPlan
import com.accountabilibuddy.companion.model.DeviceReenable
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.model.SleepTimes
import kotlinx.coroutines.launch
import java.time.Duration
import java.time.LocalDate
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import java.util.Locale

/** One block "Plan day(s)" lists: the block, its device's name, whether it's on now. */
data class PlanDayBlock(val block: BlockRef, val device: String, val on: Boolean)

/**
 * "Plan day(s)" on the phone (ADR 37), the same four steps as the PC: kind of day and dates (a calendar), the night
 * before (five ways), when each device's blocks come back on (a time per device; a block left unticked gets its own
 * time; optional lift windows), then a review. [onSave] returns null when saved (it goes to the PC like an Edit plan
 * change), else why not. [loadBlocks] reads every device's blocks again (the refresh button).
 */
@Composable
fun PlanDayScreen(
    plan: DayPlan,
    blocks: List<PlanDayBlock>,
    onSave: (DayPlanRequest) -> String?,
    onClose: () -> Unit,
    /** Measured sleep (Health Connect) the phone keeps, for "use my measured sleep". */
    slept: List<LocalWindow> = emptyList(),
    loadBlocks: () -> List<PlanDayBlock> = { blocks },
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val today = remember { LocalDate.now() }
    // The phone's next alarm, for "hours of sleep + my alarm".
    val alarm = remember { com.accountabilibuddy.companion.sync.NextAlarm.read(context)?.at?.atZone(java.time.ZoneId.systemDefault())?.toLocalDateTime() }
    var step by remember { mutableStateOf(1) }
    var offDay by remember { mutableStateOf(false) }
    val dates = remember { mutableStateListOf(today.plusDays(1)) }
    var note by remember { mutableStateOf("") }
    var error by remember { mutableStateOf<String?>(null) }
    var blockList by remember { mutableStateOf(blocks) }
    var refreshing by remember { mutableStateOf(false) }

    // What the answers start from: the first chosen date's workday template (week B and custom templates included) or the Off day one.
    fun template() = com.accountabilibuddy.companion.engine.DayPlanner.baseFor(plan, offDay, dates.firstOrNull() ?: today.plusDays(1))
    fun nightOf(off: Boolean): SleepTimes? {
        val t = com.accountabilibuddy.companion.engine.DayPlanner.baseFor(plan, off, dates.firstOrNull() ?: today.plusDays(1))
        return DayPlan.fromWorkdayAndOffDay(plan.categories, emptySet(), t, t).nightBefore(today)
    }

    var bedtime by remember { mutableStateOf(nightOf(false)?.bedtime ?: LocalTime.of(23, 0)) }
    var wake by remember { mutableStateOf(nightOf(false)?.wake ?: LocalTime.of(7, 0)) }
    var byAlarm by remember { mutableStateOf(nightOf(false)?.wakeByAlarm ?: false) }
    var sleepMode by remember { mutableStateOf(SleepMode.Range) }
    var sleepMinutes by remember { mutableStateOf(480) }

    // Blocks: each device's agreed time (from the template), blocks with their own time (unticked), lifts.
    val deviceTimes = remember { mutableStateMapOf<String, LocalTime>() }
    val ownTimes = remember { mutableStateMapOf<BlockRef, LocalTime>() }
    val lifted = remember { mutableStateListOf<BlockRef>() }
    var liftsOpen by remember { mutableStateOf(false) }
    var liftFrom by remember { mutableStateOf(LocalTime.MIDNIGHT) }
    var liftTo by remember { mutableStateOf(template().rules.reenableAt) }
    var auto by remember { mutableStateOf(false) }
    var headsUp by remember { mutableStateOf((template().rules.warnMinutes ?: 0) > 0) }

    fun deviceTime(device: String) = deviceTimes[device] ?: template().rules.reenableFor(device)

    fun setKind(off: Boolean) {
        offDay = off
        nightOf(off)?.let { bedtime = it.bedtime; wake = it.wake; byAlarm = it.wakeByAlarm }
        deviceTimes.clear()
        liftTo = template().rules.reenableAt
        headsUp = (template().rules.warnMinutes ?: 0) > 0
    }

    // The alarm counts for the first chosen morning when it rings that day.
    fun alarmFor(): LocalTime? = alarm?.takeIf { dates.isNotEmpty() && it.toLocalDate() == dates.min() }?.toLocalTime()
    fun measured(): SleepTimes? = dates.sorted().firstNotNullOfOrNull { DayPlanner.measuredNight(slept, it) }

    fun sleep(): SleepTimes? = when (sleepMode) {
        SleepMode.Range -> SleepTimes(bedtime, wake, byAlarm)
        SleepMode.HoursAndWake -> SleepTimes(wake.minusMinutes(sleepMinutes.toLong()), wake)
        SleepMode.HoursAndAlarm -> (alarmFor() ?: wake).let { up -> SleepTimes(up.minusMinutes(sleepMinutes.toLong()), up, wakeByAlarm = true) }
        SleepMode.Measured -> measured()
        SleepMode.Blank -> null
    }

    fun request(): DayPlanRequest {
        val rules = template().rules
        val lifts = if (lifted.isEmpty()) rules.lifts else listOf(BlockLift(liftFrom, liftTo, if (auto) BlockLift.AUTO else BlockLift.ALLOW, lifted.toList()))
        val devices = blockList.map { it.block.device }.distinct()
        return DayPlanRequest(
            dates.sorted(), offDay, sleep(),
            rules.copy(
                warnMinutes = if (headsUp) rules.warnMinutes ?: 5 else null,
                lifts = lifts,
                deviceTimes = devices.map { DeviceReenable(it, deviceTime(it)) }.takeIf { it.isNotEmpty() } ?: rules.deviceTimes,
                blockTimes = ownTimes.map { (ref, at) -> BlockReenable(ref, at) }.takeIf { it.isNotEmpty() },
            ),
            note,
            noSleep = sleepMode == SleepMode.Blank,
        )
    }

    fun refresh() {
        refreshing = true
        scope.launch {
            runCatching { com.accountabilibuddy.companion.blocks.BlockGuard.run(context) }
            (context.applicationContext as CompanionApp).refreshNow {
                blockList = loadBlocks()
                refreshing = false
            }
        }
    }

    BackHandler { if (step > 1) step-- else onClose() }
    Column(Modifier.fillMaxSize().padding(16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text("Plan day(s)", style = MaterialTheme.typography.headlineSmall, modifier = Modifier.weight(1f))
            TextButton(onClick = onClose) { Text("Cancel") }
        }
        Text(
            when (step) {
                1 -> "1 of 4 · What kind of day?"
                2 -> "2 of 4 · Sleep the night before"
                3 -> "3 of 4 · When blocks come back on"
                else -> "4 of 4 · Check and save"
            },
            style = MaterialTheme.typography.titleMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Spacer(Modifier.padding(4.dp))
        Column(Modifier.weight(1f).verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            when (step) {
                1 -> {
                    Panel {
                        Choice("Adjusted workday (from your Workdays plan)", !offDay) { setKind(false) }
                        Choice("Off-day (from your Off-Days plan)", offDay) { setKind(true) }
                    }
                    Panel(title = "Dates", icon = Icons.Outlined.CalendarMonth) {
                        Text("Tap one day or several.", color = MaterialTheme.colorScheme.onSurfaceVariant)
                        MonthCalendar(dates.toSet(), { date -> if (date in dates) dates.remove(date) else dates.add(date) }, from = today)
                        DateChips(dates.sorted())
                    }
                    OutlinedTextField(note, { note = it }, label = { Text("Note (optional)") }, modifier = Modifier.fillMaxWidth(), singleLine = true)
                }
                2 -> {
                    Panel(title = "How do you want to set it?", icon = Icons.Outlined.Bedtime) {
                        Choice("Bed and wake times", sleepMode == SleepMode.Range) { sleepMode = SleepMode.Range }
                        Choice("Hours of sleep + a wake time", sleepMode == SleepMode.HoursAndWake) { sleepMode = SleepMode.HoursAndWake }
                        Choice("Hours of sleep + my alarm", sleepMode == SleepMode.HoursAndAlarm) { sleepMode = SleepMode.HoursAndAlarm }
                        Choice("My measured sleep (Health Connect)", sleepMode == SleepMode.Measured) { sleepMode = SleepMode.Measured }
                        Choice("Leave sleep blank (not recommended)", sleepMode == SleepMode.Blank) { sleepMode = SleepMode.Blank }
                    }
                    Panel(tinted = true) {
                        when (sleepMode) {
                            SleepMode.Range -> {
                                Labeled("Bed by") { TimeWheel(bedtime, { bedtime = it }) }
                                Labeled("Up at") { TimeWheel(wake, { wake = it }) }
                                Row(verticalAlignment = Alignment.CenterVertically) {
                                    Text("Follow my alarm that morning", modifier = Modifier.weight(1f))
                                    Switch(byAlarm, { byAlarm = it })
                                }
                            }
                            SleepMode.HoursAndWake -> {
                                Labeled("Hours of sleep") { HoursWheel(sleepMinutes) { sleepMinutes = it } }
                                Labeled("Up at") { TimeWheel(wake, { wake = it }) }
                            }
                            SleepMode.HoursAndAlarm -> {
                                Labeled("Hours of sleep") { HoursWheel(sleepMinutes) { sleepMinutes = it } }
                                if (alarmFor() == null) {
                                    Text("No alarm set for that morning yet: this bedtime holds until it's set, then it follows your alarm.",
                                        color = MaterialTheme.colorScheme.onSurfaceVariant)
                                }
                            }
                            SleepMode.Measured -> if (measured() == null) {
                                Text("No measured sleep for that night yet. Health Connect has it once the night is over (today and past days).",
                                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                            SleepMode.Blank -> Text("No bedtime reminders, sleep count or smart bedtime for those nights.", color = MaterialTheme.colorScheme.error)
                        }
                        sleep()?.let { NightLine(it) }
                    }
                }
                3 -> {
                    Panel(
                        title = "Back on at",
                        icon = Icons.Outlined.Block,
                        action = {
                            if (refreshing) CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
                            else IconButton(onClick = { refresh() }) { Icon(Icons.Outlined.Refresh, contentDescription = "Look for new blocks") }
                        },
                    ) {
                        Text("Ticked blocks come back on at their device's time. Untick one to give it its own time.",
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    if (blockList.isEmpty()) {
                        Panel(tinted = true) {
                            Text("No blocks seen yet: Curbox on this phone and Cold Turkey on your PCs show up once they've synced.",
                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }
                    blockList.groupBy { it.block.device to it.device }.forEach { (key, items) ->
                        val (device, name) = key
                        DeviceCard(
                            name = name,
                            isPhone = device == BlockRef.PHONE,
                            time = deviceTime(device),
                            onTime = { deviceTimes[device] = it },
                        ) {
                            items.forEach { item ->
                                val own = ownTimes[item.block]
                                Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(start = 12.dp)) {
                                    Checkbox(own == null, { ticked ->
                                        if (ticked) ownTimes.remove(item.block) else ownTimes[item.block] = deviceTime(device)
                                    })
                                    Column(Modifier.weight(1f)) {
                                        Text(item.block.name)
                                        Text(if (item.on) "→ Enabled" else "→ Disabled",
                                            color = if (item.on) Color(0xFF22C55E) else MaterialTheme.colorScheme.onSurfaceVariant,
                                            style = MaterialTheme.typography.bodySmall)
                                    }
                                    if (own != null) TimeButton(own) { ownTimes[item.block] = it }
                                }
                            }
                        }
                    }
                    Panel {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Text("Heads-up 5 minutes before, on the device", modifier = Modifier.weight(1f))
                            Switch(headsUp, { headsUp = it })
                        }
                        Text("12:00 AM = midnight, the end of the day.", style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    if (blockList.isNotEmpty()) {
                        Panel(tinted = true) {
                            Row(
                                verticalAlignment = Alignment.CenterVertically,
                                modifier = Modifier.fillMaxWidth().clickable { liftsOpen = !liftsOpen },
                            ) {
                                Text("Lift some blocks for part of the day (optional)", fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                                Icon(if (liftsOpen) Icons.Outlined.KeyboardArrowUp else Icons.Outlined.KeyboardArrowDown, contentDescription = null)
                            }
                            if (liftsOpen) {
                                blockList.forEach { item ->
                                    Row(verticalAlignment = Alignment.CenterVertically) {
                                        Checkbox(item.block in lifted, { on -> if (on) lifted.add(item.block) else lifted.remove(item.block) })
                                        Text("${item.device} · ${item.block.name}")
                                    }
                                }
                                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                    Text("From")
                                    TimeButton(liftFrom) { liftFrom = it }
                                    Text("until")
                                    TimeButton(liftTo) { liftTo = it }
                                }
                                Choice("Allow: I may turn them off myself then", !auto) { auto = false }
                                Choice("Auto: turn them off for me when it starts", auto) { auto = true }
                            }
                        }
                    }
                }
                else -> Review(plan, request(), blockList, lifted)
            }
            error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth().padding(top = 8.dp)) {
            if (step > 1) OutlinedButton(onClick = { step-- }) { Text("Back") }
            Spacer(Modifier.weight(1f))
            if (step < 4) {
                Button(
                    onClick = { step++ },
                    enabled = (step != 1 || dates.isNotEmpty()) && (step != 2 || sleepMode != SleepMode.Measured || measured() != null),
                ) { Text("Next") }
            } else {
                Button(onClick = { error = onSave(request()); if (error == null) onClose() }) { Text("Save") }
            }
        }
    }
}

/** How the night before is set in "Plan day(s)". */
private enum class SleepMode { Range, HoursAndWake, HoursAndAlarm, Measured, Blank }

/** A section: a card with a title (and icon, and an action at the top right); [tinted] for options under a choice. */
@Composable
private fun Panel(
    title: String? = null,
    icon: ImageVector? = null,
    tinted: Boolean = false,
    action: (@Composable () -> Unit)? = null,
    content: @Composable ColumnScope.() -> Unit,
) {
    Card(
        Modifier.fillMaxWidth(),
        colors = CardDefaults.cardColors(
            containerColor = if (tinted) MaterialTheme.colorScheme.secondaryContainer.copy(alpha = 0.5f) else MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.6f),
        ),
    ) {
        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            if (title != null) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    icon?.let { Icon(it, contentDescription = null, modifier = Modifier.size(20.dp)); Spacer(Modifier.width(8.dp)) }
                    Text(title, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                    action?.invoke()
                }
            }
            content()
        }
    }
}

/** One device: its icon and name, its agreed time, and its blocks under it. */
@Composable
private fun DeviceCard(name: String, isPhone: Boolean, time: LocalTime, onTime: (LocalTime) -> Unit, content: @Composable ColumnScope.() -> Unit) {
    Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.primaryContainer.copy(alpha = 0.35f))) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(if (isPhone) Icons.Outlined.Smartphone else Icons.Outlined.Computer, contentDescription = null, modifier = Modifier.size(22.dp))
                Spacer(Modifier.width(8.dp))
                Text(name.uppercase(Locale.getDefault()), fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
                TimeButton(time, onTime)
            }
            HorizontalDivider()
            content()
        }
    }
}

/** A time as a button that opens a time wheel. */
@Composable
private fun TimeButton(time: LocalTime, onChange: (LocalTime) -> Unit) {
    var open by remember { mutableStateOf(false) }
    OutlinedButton(onClick = { open = true }) { Text(timeText(time)) }
    if (open) {
        var picked by remember(time) { mutableStateOf(time) }
        AlertDialog(
            onDismissRequest = { open = false },
            title = { Text("Back on at") },
            text = { Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.fillMaxWidth()) { TimeWheel(picked, { picked = it }) } },
            confirmButton = { TextButton(onClick = { onChange(picked); open = false }) { Text("OK") } },
            dismissButton = { TextButton(onClick = { open = false }) { Text("Cancel") } },
        )
    }
}

@Composable
private fun DateChips(dates: List<LocalDate>) {
    if (dates.isEmpty()) {
        Text("Pick at least one day.", color = MaterialTheme.colorScheme.error)
        return
    }

    FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
        dates.forEach { AssistChip(onClick = {}, label = { Text(dateText(it)) }) }
    }
}

/** "11:00 PM → 7:00 AM" large, with the length under it. */
@Composable
private fun NightLine(night: SleepTimes) {
    Column {
        Text("${timeText(night.bedtime)} → ${timeText(night.wake)}", style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold)
        Text(sleepText(night.bedtime, night.wake) + if (night.wakeByAlarm) " · follows your alarm" else "",
            color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

/** The review: what the days will be, as cards instead of sentences. */
@Composable
private fun Review(plan: DayPlan, request: DayPlanRequest, blocks: List<PlanDayBlock>, lifted: List<BlockRef>) {
    Panel(title = if (request.offDay) "Off-day" else "Adjusted workday", icon = Icons.Outlined.CalendarMonth) {
        DateChips(request.dates)
        request.note?.takeIf { it.isNotBlank() }?.let { Text("“${it.trim()}”", color = MaterialTheme.colorScheme.onSurfaceVariant) }
    }
    Panel(title = "The night before", icon = Icons.Outlined.Bedtime) {
        if (request.noSleep) {
            Text("No sleep planned", color = MaterialTheme.colorScheme.error, fontWeight = FontWeight.SemiBold)
        } else {
            request.sleep?.let { NightLine(it) } ?: Text("As your plan has it")
        }
    }
    val rules = request.blocks!!
    Panel(title = "Blocks come back on", icon = Icons.Outlined.Block) {
        if (blocks.isEmpty()) Text("${timeText(rules.reenableAt)} (no blocks seen yet)")
        blocks.groupBy { it.block.device to it.device }.forEach { (key, items) ->
            val (device, name) = key
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(if (device == BlockRef.PHONE) Icons.Outlined.Smartphone else Icons.Outlined.Computer, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(6.dp))
                Text(name, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                Text(timeText(rules.reenableFor(device)), fontWeight = FontWeight.SemiBold)
            }
            items.mapNotNull { item -> rules.blockTimes?.firstOrNull { it.block.same(item.block) }?.let { item.block.name to it.at } }.forEach { (block, at) ->
                Row(Modifier.padding(start = 24.dp)) {
                    Text(block, modifier = Modifier.weight(1f), color = MaterialTheme.colorScheme.onSurfaceVariant)
                    Text(timeText(at), color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }
        rules.warnMinutes?.let { Text("Heads-up $it min before", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant) }
        if (lifted.isNotEmpty()) {
            val lift = rules.lifts.first()
            HorizontalDivider()
            Text("Lifted ${timeText(lift.start)}–${timeText(lift.end)} · ${if (lift.mode == BlockLift.AUTO) "turned off for you" else "you may turn them off"}",
                fontWeight = FontWeight.SemiBold)
            Text(lifted.joinToString(", ") { it.name }, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
    DayPlanner.clashes(plan, request.dates).takeIf { it.isNotEmpty() }?.let { clash ->
        Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.errorContainer)) {
            Row(Modifier.padding(14.dp), verticalAlignment = Alignment.CenterVertically) {
                Icon(Icons.Outlined.WarningAmber, contentDescription = null)
                Spacer(Modifier.width(8.dp))
                Text("Replaces the plan already set for ${clash.joinToString(", ") { dateText(it) }}")
            }
        }
    }
    Text("Saving makes a one-off day for each date and sends it to your PC; Workdays and Off-Days stay as they are.",
        style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
}

/** Hours of sleep on a wheel: 4 h to 12 h, a quarter hour apart. */
@Composable
private fun HoursWheel(minutes: Int, onChange: (Int) -> Unit) {
    val values = remember { (240..720 step 15).toList() }
    val labels = remember { values.map { "${it / 60} h ${"%02d".format(it % 60)}" } }
    WheelPicker(labels, values.indexOf(minutes).coerceAtLeast(0), { onChange(values[it]) }, width = 120.dp)
}

@Composable
private fun Choice(text: String, selected: Boolean, onClick: () -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth().clickable(onClick = onClick)) {
        RadioButton(selected, onClick)
        Text(text)
    }
}

@Composable
private fun Labeled(label: String, content: @Composable () -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Text(label, modifier = Modifier.width(110.dp))
        content()
    }
}

private val dateFormat = DateTimeFormatter.ofPattern("EEE M/d", Locale.US)
private val timeFormat = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

private fun dateText(date: LocalDate) = date.format(dateFormat)

private fun timeText(time: LocalTime) = if (time == LocalTime.MIDNIGHT) "Midnight" else time.format(timeFormat)

private fun sleepText(bed: LocalTime, wake: LocalTime): String {
    var length = Duration.between(bed, wake)
    if (!length.isPositive) length = length.plusDays(1)
    return "${length.toHours()} h ${"%02d".format(length.toMinutesPart())} m of sleep"
}
