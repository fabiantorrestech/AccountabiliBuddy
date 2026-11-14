package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.LunchListening
import com.accountabilibuddy.shared.sync.LunchWindow
import com.accountabilibuddy.shared.sync.SyncLunch
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneOffset

/**
 * The LU1-fix parity cases (area `lu1`): the "listen fast" rule and the future-stamp clamp against the tables the C# checks assert on the PC's
 * Core `LunchListening` (`Lu1Fixtures`, written by hand, not computed). Times are minutes after 2026-11-13 00:00 UTC; local time = UTC here.
 */
object Lu1Cases {
    private val day: Instant = LocalDateTime.of(2026, 11, 13, 0, 0).toInstant(ZoneOffset.UTC)

    private fun at(minutes: Int): Instant = day.plusSeconds(minutes * 60L)

    private fun lunch(node: JsonElement): SyncLunch? {
        if (node is JsonNull) return null
        val o = node.jsonObject
        val edited = o["edited"]?.takeIf { it !is JsonNull }?.jsonPrimitive?.int
        return SyncLunch(at(o.getValue("start").jsonPrimitive.int), at(o.getValue("end").jsonPrimitive.int), edited?.let { at(it) })
    }

    private fun local(minutes: Int): LocalDateTime = LocalDateTime.ofInstant(at(minutes), ZoneOffset.UTC)

    fun listening(case: FixtureCase): String? {
        val now = case.input.getValue("now").jsonPrimitive.int
        val planned = case.input.getValue("planned").jsonArray.map {
            val o = it as JsonObject
            LunchWindow(local(o.getValue("start").jsonPrimitive.int), local(o.getValue("end").jsonPrimitive.int))
        }
        val got = LunchListening.isListening(
            at(now), local(now), lunch(case.input.getValue("own")), case.input.getValue("received").jsonArray.map { lunch(it) }, planned,
            case.input.getValue("workday").jsonPrimitive.boolean,
        )
        val expected = case.expected.jsonPrimitive.boolean
        return if (got == expected) null else "expected $expected, got $got"
    }

    fun clamp(case: FixtureCase): String? {
        val now = case.input.getValue("now").jsonPrimitive.int
        val clamped = LunchListening.clampFuture(lunch(case.input.getValue("lunch"))!!, at(now))
        val got = clamped.editedAt?.let { ((it.epochSecond - day.epochSecond) / 60).toInt() }
        val expected = case.expected.takeIf { it !is JsonNull }?.jsonPrimitive?.int
        return if (got == expected) null else "expected $expected, got $got"
    }
}
