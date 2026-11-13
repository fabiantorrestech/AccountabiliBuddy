package com.accountabilibuddy.companion.goals

import android.content.Context
import com.accountabilibuddy.shared.TokenVault
import com.accountabilibuddy.shared.goals.GoalHistoryState
import com.accountabilibuddy.shared.goals.GoalHistoryStateHolder
import com.accountabilibuddy.shared.goals.GoalHistoryStateJson
import java.io.File
import kotlinx.serialization.json.Json

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
