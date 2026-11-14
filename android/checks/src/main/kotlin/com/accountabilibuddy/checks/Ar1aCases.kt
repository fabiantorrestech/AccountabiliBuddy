package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.SessionNoteRules
import com.accountabilibuddy.shared.sync.SyncFileJson
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonPrimitive
import java.time.OffsetDateTime

/**
 * The AR1a parity cases (area `ar1a`, written by the C# checks' `Ar1aFixtures`): the phone's session note rules against Core's
 * `SessionNoteRules`, over the same made-up notes. The merged, cleaned notes are compared as the wire writes them (names in order, times,
 * nulls left out); which notes a session shows as the texts.
 */
object Ar1aCases {
    fun merge(case: FixtureCase): String? {
        val a = SyncFileJson.readSessionNotes(case.input.getValue("a")).orEmpty()
        val b = SyncFileJson.readSessionNotes(case.input.getValue("b")).orEmpty()
        val got = SyncFileJson.writeSessionNotes(SessionNoteRules.merge(a, b, OffsetDateTime.parse(case.text("now")).toInstant()))
        return JsonParity.difference(case.expected, got)
    }

    fun between(case: FixtureCase): String? {
        val notes = SyncFileJson.readSessionNotes(case.input.getValue("notes")).orEmpty()
        val got = SessionNoteRules.between(
            notes, case.text("self"), OffsetDateTime.parse(case.text("from")).toInstant(), OffsetDateTime.parse(case.text("to")).toInstant(),
        )
        val expected = case.expected.jsonArray.map { it.jsonPrimitive.content }
        return if (got == expected) null else "expected $expected, got $got"
    }

    fun clean(case: FixtureCase): String? {
        val read = SyncFileJson.readSessionNotes(case.input.getValue("notes")).orEmpty()
        val got: JsonArray = SyncFileJson.writeSessionNotes(SessionNoteRules.clean(read))
        return JsonParity.difference(case.expected, got)
    }
}
