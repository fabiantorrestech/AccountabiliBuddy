package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.LunchDay
import com.accountabilibuddy.shared.sync.LunchDelivery
import com.accountabilibuddy.shared.sync.LunchDiscardSetting
import com.accountabilibuddy.shared.sync.LunchPeer
import com.accountabilibuddy.shared.sync.LunchPiece
import com.accountabilibuddy.shared.sync.SyncFileJson
import com.accountabilibuddy.shared.sync.SyncLunch
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.long
import java.time.Duration
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneOffset

/**
 * The LU1a parity cases (area `lu1a`, ADR 88): the day's lunch rule, what a device takes from a file, equality, the delivery comparison, the
 * per-mode discard setting and the lunch's sync JSON, against the tables the C# checks assert on the PC's Core (`Lu1aFixtures`, written by hand,
 * not computed). Times are minutes after 2026-11-13 00:00 UTC.
 */
object Lu1aCases {
    private val day: Instant = LocalDateTime.of(2026, 11, 13, 0, 0).toInstant(ZoneOffset.UTC)

    private fun at(minutes: Int): Instant = day.plusSeconds(minutes * 60L)

    private fun minutes(time: Instant): Int = ((time.epochSecond - day.epochSecond) / 60).toInt()

    private fun intOrNull(node: JsonElement?): Int? = if (node == null || node is JsonNull) null else node.jsonPrimitive.int

    /** A fixture's tick count (100 ns, as C# counts); 0 when absent. */
    private fun ticks(node: JsonElement?): Long = if (node == null || node is JsonNull) 0L else node.jsonPrimitive.long

    /** A lunch from its fixture form (minutes); a `counted` that is null stays null (unknown). */
    private fun lunch(node: JsonElement?): SyncLunch? {
        if (node == null || node is JsonNull) return null
        val o = node.jsonObject
        val counted = o["counted"]?.takeIf { it !is JsonNull }?.jsonArray?.map { LunchPiece(at(it.jsonArray[0].jsonPrimitive.int), at(it.jsonArray[1].jsonPrimitive.int)) }
        return SyncLunch(
            at(o.getValue("start").jsonPrimitive.int), at(o.getValue("end").jsonPrimitive.int).plusNanos(ticks(o["endTicks"]) * 100), intOrNull(o["edited"])?.let { at(it) },
            intOrNull(o["allowance"]), counted, o.getValue("discarded").jsonPrimitive.boolean,
        )
    }

    /** What the Kotlin result looks like in the fixture's form, to compare with what the C# side wrote. */
    private fun show(lunch: SyncLunch?): String =
        if (lunch == null) "none"
        else "[${minutes(lunch.start)}-${minutes(lunch.end)}+${lunch.end.epochSecond % 60}s${lunch.end.nano}n e=${lunch.editedAt?.let { minutes(it) }} a=${lunch.allowance} c=${lunch.counted?.map { minutes(it.start) to minutes(it.end) }} d=${lunch.discarded}]"

    private fun same(got: SyncLunch?, expected: SyncLunch?): String? = if (got == expected) null else "expected ${show(expected)}, got ${show(got)}"

    fun ops(case: FixtureCase): String? {
        val now = at(case.input.getValue("now").jsonPrimitive.int)
        val lunch = lunch(case.input["lunch"])
        val number = case.input.getValue("number").jsonPrimitive.int
        val allowed = case.input.getValue("allowed").jsonPrimitive.boolean
        val fallback = case.input.getValue("fallback").jsonPrimitive.int
        val numberNanos = ticks(case.input["numberTicks"]) * 100
        val expected = case.expected.jsonObject
        return when (val verb = case.input.getValue("verb").jsonPrimitive.content) {
            "begin" -> same(LunchDay.begin(now, Duration.ofMinutes(number.toLong()).plusNanos(numberNanos)), lunch(expected["lunch"]))
            "count" -> same(LunchDay.count(lunch!!, now), lunch(expected["lunch"]))
            "startAgain" -> same(LunchDay.startAgain(lunch!!, now, fallback), lunch(expected["lunch"]))
            "discard" -> same(LunchDay.discard(lunch!!, now, allowed), lunch(expected["lunch"]))
            "adjust" -> same(LunchDay.adjust(lunch!!, now, Duration.ofMinutes(number.toLong()).plusNanos(numberNanos), fallback), lunch(expected["lunch"]))
            "held" -> same(LunchDay.held(lunch!!), lunch(expected["lunch"]))
            "remaining" -> {
                val got = LunchDay.remaining(lunch!!, now, fallback).toMinutes().toInt()
                val want = expected.getValue("minutes").jsonPrimitive.int
                if (got == want) null else "expected $want min, got $got"
            }
            "canStart" -> flag(LunchDay.canStart(lunch, now, fallback), expected)
            "canDiscard" -> flag(LunchDay.canDiscard(lunch, now, allowed), expected)
            "autoStart" -> flag(LunchDay.autoStartPending(lunch), expected)
            else -> "unknown verb $verb"
        }
    }

    private fun flag(got: Boolean, expected: JsonObject): String? {
        val want = expected.getValue("flag").jsonPrimitive.boolean
        return if (got == want) null else "expected $want, got $got"
    }

    fun receive(case: FixtureCase): String? {
        val got = LunchDay.receive(lunch(case.input["ours"]), lunch(case.input["received"])!!, at(case.input.getValue("now").jsonPrimitive.int))
        return same(got, lunch(case.expected))
    }

    /** Equality is structural: equal values, equal hashes, and a different value is not equal. */
    fun equality(case: FixtureCase): String? {
        val a = lunch(case.input["a"])!!
        val b = lunch(case.input["b"])!!
        val expected = case.expected.jsonPrimitive.boolean
        if ((a == b) != expected) return "expected equal=$expected, got ${a == b}"
        if (expected && a.hashCode() != b.hashCode()) return "equal lunches with different hashes"
        return null
    }

    fun delivery(case: FixtureCase): String? {
        val now = at(case.input.getValue("now").jsonPrimitive.int)
        val ours = lunch(case.input["ours"])!!
        val theirs = lunch(case.input["theirs"])!!
        val has = LunchDelivery.missing(ours, listOf(LunchPeer("Phone", now, theirs)), now).isEmpty()
        val expected = case.expected.jsonPrimitive.boolean
        return if (has == expected) null else "expected $expected, got $has"
    }

    fun setting(case: FixtureCase): String? {
        val input = case.input
        val pendingFrom = intOrNull(input["pendingFrom"])?.let { at(it) }
        val pending = input["pending"]?.takeIf { it !is JsonNull }?.jsonPrimitive?.boolean
        val start = LunchDiscardSetting(input.getValue("allowed").jsonPrimitive.boolean, pending, pendingFrom)
        val expected = case.expected.jsonObject
        val now = at(input.getValue("now").jsonPrimitive.int)
        val session = input.getValue("session").jsonPrimitive.boolean
        val (got, outcome) = if (input.getValue("verb").jsonPrimitive.content == "change") {
            start.change(input.getValue("wanted").jsonPrimitive.boolean, now, Duration.ofMinutes(input.getValue("wait").jsonPrimitive.int.toLong()), session)
        } else {
            start.applyIfReady(now, session) to null
        }

        if (outcome != null && !outcome.name.equals(expected.getValue("outcome").jsonPrimitive.content, ignoreCase = true)) return "outcome ${outcome.name}"
        if (got.allowed != expected.getValue("allowed").jsonPrimitive.boolean) return "allowed ${got.allowed}"
        val wantPending = expected["pending"]?.takeIf { it !is JsonNull }?.jsonPrimitive?.boolean
        if (got.pending != wantPending) return "pending ${got.pending}"
        val wantFrom = intOrNull(expected["pendingFrom"])
        if (expected.containsKey("pendingFrom") && wantFrom != got.pendingFrom?.let { minutes(it) }) return "pendingFrom ${got.pendingFrom}"
        return null
    }

    /**
     * The lunch's sync JSON: what the PC wrote reads here as the same lunch (and an older file with no `counted` reads as unknown, `[]` as none);
     * what this side writes has the same keys as the PC's and reads back equal.
     */
    fun wire(case: FixtureCase): String? {
        val spec = lunch(case.input["lunch"])!!
        val theirs = Json.parseToJsonElement(case.expected.jsonPrimitive.content).jsonObject
        val read = SyncFileJson.readLunch(theirs)
        if (read != spec) return "the PC's JSON reads as ${show(read)}, expected ${show(spec)}"
        val mine = SyncFileJson.writeLunch(spec)
        if (mine.keys != theirs.keys) return "keys ${mine.keys} against the PC's ${theirs.keys}"
        val again = SyncFileJson.readLunch(mine)
        if (again != spec) return "its own JSON reads back as ${show(again)}"
        val countedPieces = (mine["counted"] as? JsonArray)?.size
        if (countedPieces != spec.counted?.size) return "counted written as $countedPieces pieces"
        return null
    }
}
