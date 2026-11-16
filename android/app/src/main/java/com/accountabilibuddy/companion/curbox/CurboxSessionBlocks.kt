package com.accountabilibuddy.companion.curbox

import android.content.Context
import android.util.Log
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.placement.PlacementSession
import com.accountabilibuddy.shared.session.CurboxBlockRef
import com.accountabilibuddy.shared.session.CurboxSessionBlocksRule
import java.time.Instant

/**
 * The Curbox blocks the phone's own session profile turns on (ADR 85, PS2c): turned on while the session runs, and at its end back off only the ones this
 * session turned on (a block that was already on stays on). The decisions are [CurboxSessionBlocksRule]; this reads Curbox, calls it and keeps the memory in
 * the "curbox" prefs. Emergency time lifts them like the focus group (the phone's live way out); they come back after it. Runs from [CurboxHardMode.check].
 */
object CurboxSessionBlocks {
    private const val TAG = "CurboxSessionBlocks"
    private const val PREFS = "curbox"
    private const val KEY_OURS = "sessionBlocksOurs"
    private const val KEY_BEFORE = "sessionBlocksBefore"

    suspend fun check(context: Context, now: Instant) {
        val app = context.applicationContext as CompanionApp
        val record = PlacementSession.ownRecord(app)
        // The blocks the session's profile had when it started (the copy in the record, never the live profile).
        val wanted = record?.snapshot?.curboxBlocks.orEmpty()
        val running = record != null && !app.emergency.isActive(now)
        val memory = load(context)
        // Nothing wanted and nothing remembered: Curbox is not even asked.
        if (memory.isEmpty && (!running || wanted.isEmpty())) return

        val client = CurboxClient(context)
        val current = client.blockers()?.associate { CurboxBlockRef(it.kind, it.id) to it.on }
        val step = CurboxSessionBlocksRule.step(running, wanted, current, memory)
        val failed = step.actions.filterNot { action ->
            client.set(action.block.kind, action.block.id, action is CurboxSessionBlocksRule.Action.TurnOn)
        }
        if (step.actions.isNotEmpty()) Log.i(TAG, "${step.actions.size} block(s) asked, ${failed.size} refused")
        save(context, CurboxSessionBlocksRule.settle(step, failed))
    }

    private fun load(context: Context): CurboxSessionBlocksRule.Memory {
        val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
        fun read(key: String) = prefs.getStringSet(key, emptySet()).orEmpty().mapNotNull { text ->
            text.split('\u0001').takeIf { it.size == 2 }?.let { CurboxBlockRef(it[0], it[1]) }
        }.toSet()
        return CurboxSessionBlocksRule.Memory(read(KEY_OURS), read(KEY_BEFORE))
    }

    private fun save(context: Context, memory: CurboxSessionBlocksRule.Memory) {
        fun text(set: Set<CurboxBlockRef>) = set.map { "${it.kind}\u0001${it.id}" }.toSet()
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit()
            .putStringSet(KEY_OURS, text(memory.ours)).putStringSet(KEY_BEFORE, text(memory.onBefore)).apply()
    }
}
