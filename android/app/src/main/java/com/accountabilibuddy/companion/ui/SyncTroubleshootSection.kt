package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Error
import androidx.compose.material.icons.outlined.ExpandLess
import androidx.compose.material.icons.outlined.ExpandMore
import androidx.compose.material.icons.outlined.RemoveCircleOutline
import androidx.compose.material.icons.outlined.Schedule
import androidx.compose.material.icons.outlined.SyncProblem
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbInfoBanner
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbTextButton
import com.accountabilibuddy.companion.ui.kit.AbToggleRow
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.sync.SyncBoardBuilder
import com.accountabilibuddy.shared.sync.SyncBoardDevice
import com.accountabilibuddy.shared.sync.SyncBoardMethod
import com.accountabilibuddy.shared.sync.SyncBoardWords
import com.accountabilibuddy.shared.sync.SyncDot
import com.accountabilibuddy.shared.sync.SyncDotColor
import com.accountabilibuddy.shared.sync.SyncReceiptParts
import com.accountabilibuddy.shared.sync.SyncUnreadableFile
import com.accountabilibuddy.shared.tasks.TaskConflictPicker
import kotlinx.coroutines.launch
import java.time.Instant
import java.time.ZoneId
import java.time.ZoneOffset

/** The icon of a board dot: never colour alone. The same meaning as on the PC (CheckCircle, ClockOutline, AlertCircle, MinusCircleOutline). */
private fun iconOf(color: SyncDotColor): ImageVector = when (color) {
    SyncDotColor.GREEN -> Icons.Filled.CheckCircle
    SyncDotColor.YELLOW -> Icons.Outlined.Schedule
    SyncDotColor.RED -> Icons.Filled.Error
    SyncDotColor.GREY -> Icons.Outlined.RemoveCircleOutline
}

@Composable
private fun tintOf(color: SyncDotColor): Color = when (color) {
    SyncDotColor.GREEN -> Ab.colors.met
    SyncDotColor.YELLOW -> Ab.colors.waiting
    SyncDotColor.RED -> Ab.colors.drift
    SyncDotColor.GREY -> Ab.colors.faint
}

/**
 * Troubleshoot sync (SYNC1c, ADR 86), the last section of Settings › Devices & sync, always shown and in both modes: a per-method board of which
 * devices confirm this phone's newest file (a dot with an icon and a sentence, the items folded under each device), the files that aren't taken
 * in (Try again, Ignore, Remove my stale file), Re-read everything, Send my files again, Re-read goal history, the tasks "Changed on both
 * devices" with a picker, the day plan question, and the sync log's diagnostics report. Display and maintenance only: no lock in a focus session,
 * nothing here changes a setting or applies a looser one. With no way of syncing it says "Not syncing" and shows no red.
 *
 * @param sessionLock Why the Day plan can't be changed now (a focus session the user started); null when it can.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun SyncTroubleshootSection(ui: SyncTroubleshootUi, joined: Boolean, sessionLock: String?) {
    val troubleshoot = ui.troubleshoot
    // Anything the board shows changing makes it build again.
    val version by ui.version.collectAsState()
    val busy by troubleshoot.busy.collectAsState()
    val taskLists by ui.taskLists.collectAsState()
    val planClash by ui.dayPlanClash.collectAsState()
    val board = remember(version) { troubleshoot.board() }
    val files = remember(version) { troubleshoot.files() }
    val conflicts = remember(taskLists) { TaskConflictPicker.rows(taskLists) }
    val scope = rememberCoroutineScope()
    var result by remember { mutableStateOf<String?>(null) }
    var reportResult by remember { mutableStateOf<String?>(null) }
    var removing by remember { mutableStateOf<SyncUnreadableFile?>(null) }
    var picking by remember { mutableStateOf<String?>(null) }
    var receiptsOn by remember { mutableStateOf(ui.receiptsSwitch.first()) }
    val zone = ZoneId.systemDefault()
    val now = Instant.now().atOffset(ZoneOffset.UTC)

    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        AbSectionLabel("Troubleshoot sync", Modifier.padding(horizontal = 4.dp))
        Text(
            "See which of your devices have your newest file, read everything again, send your files again, and answer what changed on two devices. " +
                "Nothing here changes a setting, and it all works during a focus session.",
            style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(horizontal = 4.dp),
        )

        if (!joined || board.notSyncing) {
            AbInfoBanner("Not syncing · set up a sync method above. Nothing is wrong: this phone works alone.", icon = Icons.Outlined.SyncProblem)
        } else if (board.noOtherDevices) {
            AbInfoBanner("No other devices yet · link one, then this board shows who has your files.", icon = Icons.Outlined.SyncProblem)
        } else {
            board.methods.forEach { method -> BoardMethodCard(method, now, zone) }
        }

        // Files that aren't taken in.
        if (joined && files.isNotEmpty()) {
            AbCard(contentPadding = PaddingValues(14.dp), verticalSpacing = 10.dp) {
                Text("Files that aren't taken in", style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold, color = Ab.colors.ink)
                files.forEach { file ->
                    UnreadableRow(
                        file, now, zone, deviceName = file.device?.let { id -> ui.deviceNameOf(id.toString()) },
                        onTryAgain = { scope.launch { result = troubleshoot.tryAgain(file) } },
                        onIgnore = { troubleshoot.ignore(file) },
                        onRemove = { removing = file },
                    )
                }
            }
        }

        // The three actions, one result line.
        AbCard(contentPadding = PaddingValues(14.dp), verticalSpacing = 8.dp) {
            Text("Fix and re-send", style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold, color = Ab.colors.ink)
            FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                AbTonalButton("Re-read everything", onClick = { scope.launch { result = troubleshoot.rereadAll() } }, enabled = joined && !busy)
                AbTonalButton("Send my files again", onClick = { scope.launch { result = troubleshoot.sendAgain() } }, enabled = joined && !busy)
                AbTonalButton(troubleshoot.rereadHistoryText(), onClick = { scope.launch { result = troubleshoot.rereadHistory() } }, enabled = joined && !busy)
            }
            Text(
                if (busy) "Working…" else result ?: "Re-reading merges every file again: nothing changes that was already merged. The relay is read at most every 5 minutes.",
                style = Ab.type.small, color = Ab.colors.muted,
            )
        }

        // Changed on both devices.
        AbCard(contentPadding = PaddingValues(14.dp), verticalSpacing = 8.dp) {
            Text("Changed on both devices", style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold, color = Ab.colors.ink)
            if (conflicts.isEmpty() && !planClash) {
                Text("Nothing waits for an answer.", style = Ab.type.small, color = Ab.colors.muted)
            }

            conflicts.forEach { row ->
                Row(
                    Modifier.fillMaxWidth().heightIn(min = 48.dp).clickable(role = Role.Button) { picking = row.id }.semantics { contentDescription = "Pick a version: ${row.title}" },
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Column(Modifier.weight(1f)) {
                        Text(row.title.ifBlank { "(untitled task)" }, style = Ab.type.bodyMedium, color = Ab.colors.ink, maxLines = 2)
                        Text(if (row.count == 1) "1 version waits" else "${row.count} versions wait", style = Ab.type.small, color = Ab.colors.muted)
                    }
                    Text("Pick", style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold, color = Ab.colors.ink)
                }
            }

            if (planClash) {
                Column(Modifier.fillMaxWidth()) {
                    Text("The day plan changed in two places", style = Ab.type.bodyMedium, color = Ab.colors.ink)
                    // The Day plan is locked during a focus session the user started: an answer is a Day plan change.
                    Text(if (sessionLock != null) "Answer after the session. $sessionLock" else "Waiting for your answer.", style = Ab.type.small, color = Ab.colors.muted)
                }
            }

            Text("Goals merge per field; the newest edit wins, so they have no conflicts.", style = Ab.type.small, color = Ab.colors.muted)
        }

        // The sync log and its report.
        AbCard(contentPadding = PaddingValues(14.dp), verticalSpacing = 8.dp) {
            Text("Sync log", style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold, color = Ab.colors.ink)
            Text(
                "This phone keeps the last 7 days of sync passes: a time, a way, and counts. Never a title, note, name or address. " +
                    "The report is the log plus this board, saved only where you send it; nothing is sent by itself.",
                style = Ab.type.small, color = Ab.colors.muted,
            )
            AbTonalButton("Share a diagnostics report", onClick = { scope.launch { reportResult = troubleshoot.shareReport(ui.shareReport) } }, enabled = !busy)
            reportResult?.let { Text(it, style = Ab.type.small, color = Ab.colors.muted) }
        }

        AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
            // For testing (developer): a notification when sync reaches each device, from the device cards' "seen"; off by default.
            AbToggleRow(
                "Tell me when sync reaches each device", receiptsOn,
                { on ->
                    ui.receiptsSwitch.second(on)
                    receiptsOn = on
                },
                subtitle = "For testing: a quiet notification when another device's file shows it has this phone's latest one, and which don't yet. Every way of syncing; a watch can't confirm.",
            )
        }
    }

    removing?.let { file ->
        AbDialog(
            title = "Remove this phone's old copy?",
            onDismiss = { removing = null },
            confirmText = "Remove",
            onConfirm = {
                removing = null
                scope.launch { result = troubleshoot.removeStale(file) }
            },
        ) {
            Text(
                "It deletes this phone's own old file on ${SyncBoardBuilder.titleOf(file.method)}. Nothing of another device and nothing current is touched.",
                style = Ab.type.bodyMedium, color = Ab.colors.ink,
            )
        }
    }

    picking?.let { id ->
        TaskConflictSheet(
            lists = taskLists, taskId = id, deviceNameOf = ui.deviceNameOf,
            onAnswer = { conflict, pick -> ui.answerConflict(id, conflict, pick) },
            onDismiss = { picking = null },
        )
    }
}

/** One method of the board: its name and a row for each other device (the dot, its sentence and, folded, the items). */
@Composable
private fun BoardMethodCard(method: SyncBoardMethod, now: java.time.OffsetDateTime, zone: ZoneId) {
    AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 12.dp), verticalSpacing = 8.dp) {
        Text(method.title, style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold, color = Ab.colors.ink)
        if (method.devices.isEmpty()) Text("No other devices yet.", style = Ab.type.small, color = Ab.colors.muted)
        method.devices.forEach { device -> BoardDeviceRow(method.method, device, now, zone) }
    }
}

@Composable
private fun BoardDeviceRow(method: String, device: SyncBoardDevice, now: java.time.OffsetDateTime, zone: ZoneId) {
    var open by remember(method, device.id) { mutableStateOf(false) }
    val words = SyncBoardWords.words(device.dot)
    val detail = buildString {
        append("App ").append(device.appVersion.ifBlank { "unknown" })
        device.confirmedFile?.let { append(" · confirmed your file from ").append(SyncBoardWords.clock(it, now, zone)) }
    }
    Column(Modifier.fillMaxWidth()) {
        Row(
            Modifier.fillMaxWidth().heightIn(min = 48.dp).clickable(role = Role.Button) { open = !open }
                .semantics { contentDescription = "${device.name}: ${SyncBoardWords.colorWord(device.dot.color)}. $words" },
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            Dot(device.dot)
            Column(Modifier.weight(1f)) {
                Text(device.name, style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold, color = Ab.colors.ink)
                Text(words, style = Ab.type.small, color = Ab.colors.ink)
                Text(detail, style = Ab.type.small, color = Ab.colors.muted)
            }
            Icon(if (open) Icons.Outlined.ExpandLess else Icons.Outlined.ExpandMore, contentDescription = if (open) "Hide items" else "Show items", tint = Ab.colors.muted, modifier = Modifier.size(24.dp))
        }

        // What this phone itself refused from the device's newest file: always visible, whatever the method (red, with its own icon).
        device.refusedHere.forEach { code ->
            val text = "Couldn't apply ${SyncBoardWords.partName(code)} from ${device.name}"
            Row(
                Modifier.fillMaxWidth().padding(start = 34.dp, bottom = 4.dp).semantics { contentDescription = text },
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                Icon(Icons.Filled.Error, contentDescription = null, tint = Ab.colors.drift, modifier = Modifier.size(18.dp))
                Text(text, style = Ab.type.small, color = Ab.colors.ink, modifier = Modifier.weight(1f))
            }
        }

        if (open) {
            Column(Modifier.padding(start = 34.dp, bottom = 6.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                device.parts.forEach { part ->
                    Row(Modifier.fillMaxWidth().semantics { contentDescription = "${SyncBoardWords.partName(part.code)}: ${SyncBoardWords.words(part.dot)}" }, verticalAlignment = Alignment.Top, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        Dot(part.dot, 18)
                        Column(Modifier.weight(1f)) {
                            Text(SyncBoardWords.partName(part.code), style = Ab.type.small, fontWeight = FontWeight.SemiBold, color = Ab.colors.ink)
                            Text(SyncBoardWords.words(part.dot), style = Ab.type.small, color = Ab.colors.muted)
                        }
                    }
                }
            }
        }
    }
}

/** A dot: the colour with its own icon, so a colour is never the only signal. */
@Composable
private fun Dot(dot: SyncDot, size: Int = 24) {
    Icon(iconOf(dot.color), contentDescription = SyncBoardWords.colorWord(dot.color), tint = tintOf(dot.color), modifier = Modifier.size(size.dp))
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun UnreadableRow(
    file: SyncUnreadableFile,
    now: java.time.OffsetDateTime,
    zone: ZoneId,
    deviceName: String?,
    onTryAgain: () -> Unit,
    onIgnore: () -> Unit,
    onRemove: () -> Unit,
) {
    val who = deviceName ?: if (file.ownStale) "This phone" else "A device"
    Column(Modifier.fillMaxWidth()) {
        Text("${SyncBoardBuilder.titleOf(file.method)} · $who", style = Ab.type.bodyMedium, fontWeight = FontWeight.SemiBold, color = Ab.colors.ink)
        Text("${SyncBoardWords.reasonOfFile(file.reason)} · ${SyncBoardWords.clock(file.at, now, zone)}", style = Ab.type.small, color = Ab.colors.muted)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
            if (file.method != SyncReceiptParts.QR && file.method != SyncReceiptParts.FILE) AbTextButton("Try again", onClick = onTryAgain)
            AbTextButton("Ignore", onClick = onIgnore)
            // Only this phone's own old-group or extra copy, and never on the relay (ADR 46).
            if (file.ownStale && file.method != SyncReceiptParts.RELAY) AbTextButton("Remove my stale file", onClick = onRemove, danger = true)
        }
    }
}
