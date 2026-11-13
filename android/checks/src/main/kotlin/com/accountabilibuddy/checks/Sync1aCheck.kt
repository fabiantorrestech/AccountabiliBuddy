package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.SyncDot
import com.accountabilibuddy.shared.sync.SyncFileCodec
import com.accountabilibuddy.shared.sync.SyncFileStrip
import com.accountabilibuddy.shared.sync.SyncKey
import com.accountabilibuddy.shared.sync.SyncReceipt
import com.accountabilibuddy.shared.sync.SyncReceiptBoard
import com.accountabilibuddy.shared.sync.SyncReceiptsJson
import com.accountabilibuddy.shared.sync.SyncReceiptsLocal
import com.accountabilibuddy.shared.sync.SyncReceiverFacts
import com.accountabilibuddy.shared.sync.SyncSentFacts
import com.accountabilibuddy.shared.tasks.TaskConflict
import com.accountabilibuddy.shared.tasks.TaskConflictActions
import com.accountabilibuddy.shared.tasks.TaskJson
import com.accountabilibuddy.shared.tasks.TaskLists
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.time.OffsetDateTime
import java.util.UUID

/**
 * SYNC1a fixtures (area `sync1a`, written by the C# checks' `Sync1aChecks`): receipts in sealed files the PC made (read, cleaned and written
 * back byte for byte as the PC does, with the same read-failure reason), the colour rule's cases, the receiver-local record's operations and the
 * conflict answers ("Keep both") against the PC's `TaskActions`.
 */
class Sync1aCheck(override val name: String) : FixtureCheck {
    override val area = "sync1a"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        if (name == "receipts-file") stripChecks(fixture, report)
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "$area/$name: ${case.name}") {
                when (name) {
                    "receipts-file" -> receiptsFile(case)
                    "board" -> board(case)
                    "local" -> local(case)
                    else -> keepBoth(case)
                }
            }
        }
    }

    // The phone's strip (the PC's ForQr and its kept copy): receipts never go in a QR file, and stay in the copy a receiver keeps.
    private fun stripChecks(fixture: FixtureFile, report: CheckReport) {
        val case = fixture.cases.first { it.name.startsWith("every field of a receipt") }
        val key = SyncKey.fromSecret(hex(case.text("secret")))
        val file = SyncFileCodec.open(java.util.Base64.getDecoder().decode(case.text("bytes")), key)
        report.check("$area/$name: the sample file carries receipts", file?.receipts?.size == 2)
        if (file == null) return
        report.check("$area/$name: receipts are stripped from a QR file", SyncFileStrip.forQr(file).receipts == null)
        report.check("$area/$name: the copy a receiver keeps holds the receipts (they carry no text)", SyncFileStrip.keptCopy(file, null, null).receipts?.size == 2)
    }

    private fun hex(text: String) = ByteArray(text.length / 2) { text.substring(it * 2, it * 2 + 2).toInt(16).toByte() }

    private fun time(text: String): OffsetDateTime = OffsetDateTime.parse(text)

    private fun timeOrNull(element: JsonElement?): OffsetDateTime? = element?.takeIf { it !is JsonNull }?.jsonPrimitive?.content?.let(::time)

    private fun times(element: JsonElement?): Map<String, OffsetDateTime>? =
        element?.takeIf { it !is JsonNull }?.jsonObject?.entries?.associateTo(LinkedHashMap()) { it.key to time(it.value.jsonPrimitive.content) }

    // ---- receipts-file

    private fun receiptsFile(case: FixtureCase): String? {
        val key = SyncKey.fromSecret(hex(case.text("secret")))
        val bytes = java.util.Base64.getDecoder().decode(case.text("bytes"))
        val result = SyncFileCodec.tryOpen(bytes, key)
        val expected = case.expected.jsonObject
        val wantFailure = expected.getValue("failure").takeIf { it !is JsonNull }?.jsonPrimitive?.content
        if (result.failure?.code != wantFailure) return "failure: kotlin ${result.failure?.code} vs C# $wantFailure"
        if (wantFailure != null) return if (result.file == null) null else "opened a file that must not open"
        val file = result.file ?: return "did not open"
        val wantReceipts = expected.getValue("receipts")
        if (wantReceipts is JsonNull) return if (file.receipts == null) null else "receipts: kotlin has ${file.receipts!!.size} entries, C# none"
        val receipts = file.receipts ?: return "receipts: kotlin none, C# has some"
        // The phone writes what it read: the PC's JSON for the same file, names, order, times and all.
        return JsonParity.difference(wantReceipts, SyncReceiptsJson.write(receipts), "receipts")
    }

    // ---- board

    private fun facts(element: JsonElement): SyncReceiverFacts? {
        if (element is JsonNull) return null
        val obj = element.jsonObject
        val receipt = obj["receipt"]?.takeIf { it !is JsonNull }?.let { SyncReceiptsJson.read(JsonArray(listOf(it)))?.firstOrNull() ?: error("the receipt does not read") }
        return SyncReceiverFacts(
            obj.getValue("kind").jsonPrimitive.content,
            obj.getValue("removed").jsonPrimitive.booleanOrNull!!,
            timeOrNull(obj["writtenAt"]),
            obj.getValue("writesReceipts").jsonPrimitive.booleanOrNull!!,
            timeOrNull(obj["seen"]),
            receipt,
            times(obj["lastViaMethod"]),
        )
    }

    private fun board(case: FixtureCase): String? {
        val now = time(case.text("now"))
        val sentJson = case.input.getValue("sent").jsonObject
        val sent = SyncSentFacts(time(sentJson.getValue("newest").jsonPrimitive.content), times(sentJson["reachedAt"])!!, times(sentJson["previousSentAt"]))
        val receiver = facts(case.input.getValue("receiver"))
        val dot: SyncDot = if (case.text("kind") == "method") {
            SyncReceiptBoard.method(
                case.text("subject"), now, sent, receiver,
                case.input["intervalMinutes"]?.takeIf { it !is JsonNull }?.jsonPrimitive?.intOrNull?.let { java.time.Duration.ofMinutes(it.toLong()) },
            )
        } else {
            SyncReceiptBoard.part(case.text("subject"), now, sent, timeOrNull(case.input["reachedAny"]), receiver)
        }

        // The C# enum names (Green, CaughtUp) against the phone's (GREEN, CAUGHT_UP).
        val color = dot.color.name.lowercase().replaceFirstChar { it.uppercase() }
        val reason = dot.reason.name.lowercase().split('_').joinToString("") { it.replaceFirstChar { c -> c.uppercase() } }
        val expected = case.expected.jsonObject
        val wantColor = expected.getValue("color").jsonPrimitive.content
        val wantReason = expected.getValue("reason").jsonPrimitive.content
        return if (color == wantColor && reason == wantReason) null else "kotlin $color/$reason vs C# $wantColor/$wantReason"
    }

    // ---- local

    private fun local(case: FixtureCase): String? {
        var state = SyncReceiptsLocal()
        for (element in case.input.getValue("ops").jsonArray) {
            val op = element.jsonObject
            fun text(key: String) = op[key]?.takeIf { it !is JsonNull }?.jsonPrimitive?.content
            val from = UUID.fromString(text("from"))
            state = when (text("op")) {
                "arrival" -> state.recordArrival(from, text("method")!!, time(text("at")!!))
                "failure" -> state.recordFailure(from, text("method")!!, text("reason")!!, text("at")?.let { time(it) })
                "parts" -> state.recordParts(from, time(text("at")!!), op.getValue("states").jsonObject.entries.associateTo(LinkedHashMap()) { it.key to it.value.jsonPrimitive.content })
                else -> state.recordHistory(from, text("month")!!, time(text("at")!!))
            }
        }

        val forget = case.input.getValue("forget").jsonArray.map { UUID.fromString(it.jsonPrimitive.content) }.toSet()
        state = state.forget(forget)
        val self = UUID.fromString(case.text("self"))
        val active = case.input.getValue("active").jsonArray.map { UUID.fromString(it.jsonPrimitive.content) }.toSet()
        val expected = case.expected.jsonObject
        JsonParity.difference(expected.getValue("senders"), SyncReceiptsJson.write(state.senders), "senders")?.let { return it }
        val file: List<SyncReceipt>? = state.forFile(self, active)
        val wantFile = expected.getValue("forFile")
        if (wantFile is JsonNull) return if (file == null) null else "forFile: kotlin has ${file.size} rows, C# none"
        return JsonParity.difference(wantFile, SyncReceiptsJson.write(file ?: return "forFile: kotlin none, C# has rows"), "forFile")
    }

    // ---- keep-both

    private fun conflict(json: JsonObject) = TaskConflict(
        json.getValue("field").jsonPrimitive.content,
        json["otherValue"]?.takeIf { it !is JsonNull }?.jsonPrimitive?.content,
        json.getValue("otherDeviceId").jsonPrimitive.content,
        OffsetDateTime.parse(json.getValue("otherEditedAt").jsonPrimitive.content),
    )

    // The copy's step ids are random on the PC: both sides compare them as s0, s1, ... for the task [newId].
    private fun normalized(tasks: JsonArray, newId: String): JsonArray = JsonArray(tasks.map { element ->
        val task = element.jsonObject
        val inner = task["task"]?.jsonObject ?: return@map element
        val steps = inner["steps"] as? JsonArray
        if (!inner.getValue("id").jsonPrimitive.content.equals(newId, ignoreCase = true) || steps == null) return@map element
        val renamed = JsonArray(steps.mapIndexed { index, step -> JsonObject(step.jsonObject + ("id" to JsonPrimitive("s$index"))) })
        JsonObject(task + ("task" to JsonObject(inner + ("steps" to renamed))))
    })

    private fun keepBoth(case: FixtureCase): String? {
        val lists = TaskLists(case.input.getValue("tasks").jsonArray.map(TaskJson::readSyncTask), emptyList(), emptyList(), emptyList())
        val id = case.text("id")
        val newId = case.text("newId")
        val now = DotNetTime.cut(time(case.text("now")))
        val conflict = conflict(case.input.getValue("conflict").jsonObject)
        val self = case.text("self")
        val result = if (case.text("kind") == "both") {
            TaskConflictActions.keepBothVersions(lists, id, conflict, newId, self, now) { index -> "s$index" }
        } else {
            TaskConflictActions.resolveConflict(lists, id, conflict, case.input.getValue("keepOther").jsonPrimitive.booleanOrNull!!, self, now)
        }

        val expected = case.expected.jsonObject
        val wantAffected = expected.getValue("affected").jsonArray.map { it.jsonPrimitive.content.lowercase() }
        if (result.ids.map { it.lowercase() } != wantAffected) return "affected: kotlin ${result.ids} vs C# $wantAffected"
        val wantToast = expected.getValue("toast").jsonPrimitive.content
        if (result.toast != wantToast) return "toast: kotlin '${result.toast}' vs C# '$wantToast'"
        val actual = normalized(JsonArray(result.lists.tasks.map { TaskJson.writeSyncTask(it) }), newId)
        return JsonParity.difference(normalized(expected.getValue("tasks").jsonArray, newId), actual, "tasks")
    }
}
