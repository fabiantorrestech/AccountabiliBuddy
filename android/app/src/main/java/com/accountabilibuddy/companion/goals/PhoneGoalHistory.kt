package com.accountabilibuddy.companion.goals

import android.content.Context
import com.accountabilibuddy.shared.TokenVault
import com.accountabilibuddy.shared.goals.GoalHistoryCodec
import com.accountabilibuddy.shared.goals.GoalHistoryJson
import com.accountabilibuddy.shared.goals.GoalHistoryMonth
import com.accountabilibuddy.shared.goals.GoalHistoryState
import com.accountabilibuddy.shared.goals.GoalHistoryStateHolder
import com.accountabilibuddy.shared.goals.GoalHistoryStateJson
import com.accountabilibuddy.shared.goals.GoalHistoryYear
import com.accountabilibuddy.shared.sync.SyncFileCodec
import com.accountabilibuddy.shared.sync.SyncKey
import java.io.File
import kotlinx.serialization.json.Json

/**
 * Seals and opens the goal history files with the group's key, exactly as the PC does (ADR 79 shared history; Q732): the sync files'
 * crypto ([SyncFileCodec.sealJson]) under their own header, "ABM1" for a month and "ABY1" for a year, over the JSON the shared twins
 * write ([GoalHistoryJson]). A file that isn't this group's, can't be read or comes from a newer format opens as null.
 */
class PhoneGoalHistoryCodec(private val key: () -> SyncKey?) : GoalHistoryCodec {
    override fun seal(month: GoalHistoryMonth): ByteArray = SyncFileCodec.sealJson(json(GoalHistoryJson.writeMonth(month).toString()), key()!!, MONTH)

    override fun seal(year: GoalHistoryYear): ByteArray = SyncFileCodec.sealJson(json(GoalHistoryJson.writeYear(year).toString()), key()!!, YEAR)

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

/**
 * The goal history's state ([GoalHistoryState]) in one file sealed with the goals' Keystore key (ids, numbers and times, and the month
 * files as last written; never in a backup or a sync file). Unreadable or missing: an empty state (only a read position is lost, never a goal).
 */
class PhoneGoalHistoryState(context: Context) : GoalHistoryStateHolder {
    private val file = File(context.filesDir, FILE)
    private var state: GoalHistoryState = load()

    override val value: GoalHistoryState get() = state

    @Synchronized
    override fun reset() {
        state = GoalHistoryState()
        runCatching { file.delete() }
    }

    @Synchronized
    override fun save() {
        runCatching {
            val temp = File(file.parentFile, "$FILE.tmp")
            temp.writeText(TokenVault.seal(GoalHistoryStateJson.write(state).toString(), ALIAS))
            if (!temp.renameTo(file)) {
                file.delete()
                temp.renameTo(file)
            }
        }
    }

    private fun load(): GoalHistoryState = runCatching {
        if (!file.exists()) return@runCatching GoalHistoryState()
        val text = TokenVault.open(file.readText(), ALIAS) ?: return@runCatching GoalHistoryState()
        GoalHistoryStateJson.read(Json.parseToJsonElement(text)) ?: GoalHistoryState()
    }.getOrDefault(GoalHistoryState())

    private companion object {
        const val FILE = "goal-history-state.sealed"
        const val ALIAS = "accountabilibuddy-goals"
    }
}
