package com.accountabilibuddy.companion.goals

import com.accountabilibuddy.shared.goals.GoalHistoryCodec
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalHistoryJson
import com.accountabilibuddy.shared.goals.GoalHistoryMonth
import com.accountabilibuddy.shared.goals.GoalHistoryYear
import com.accountabilibuddy.shared.sync.SyncFileCodec
import com.accountabilibuddy.shared.sync.SyncKey
import kotlinx.serialization.json.Json

/**
 * Seals and opens the goal history files with the group's key, exactly as the PC does (ADR 79 shared history; Q732): the sync files'
 * crypto ([SyncFileCodec.sealJson]) under their own header, "ABM1" for a month and "ABY1" for a year, over the JSON the shared twins
 * write ([GoalHistoryJson]). A file that isn't this group's, can't be read or comes from a newer format opens as null.
 */
class PhoneGoalHistoryCodec(private val key: () -> SyncKey?) : GoalHistoryCodec {
    override fun seal(month: GoalHistoryMonth): ByteArray = SyncFileCodec.sealJson(json(GoalJson.text(GoalHistoryJson.writeMonth(month))), key()!!, MONTH)

    override fun seal(year: GoalHistoryYear): ByteArray = SyncFileCodec.sealJson(json(GoalJson.text(GoalHistoryJson.writeYear(year))), key()!!, YEAR)

    override fun openMonth(bytes: ByteArray): GoalHistoryMonth? = open(bytes, MONTH)?.let(GoalHistoryJson::readMonth)

    override fun openYear(bytes: ByteArray): GoalHistoryYear? = open(bytes, YEAR)?.let(GoalHistoryJson::readYear)

    private fun open(bytes: ByteArray, header: ByteArray) = key()?.let { k ->
        SyncFileCodec.openJson(bytes, k, header)?.let { runCatching { Json.parseToJsonElement(String(it, Charsets.UTF_8)) }.getOrNull() }
    }

    private fun json(text: String) = text.toByteArray(Charsets.UTF_8)

    private companion object {
        val MONTH = "ABM1".toByteArray(Charsets.US_ASCII)
        val YEAR = "ABY1".toByteArray(Charsets.US_ASCII)
    }
}
