package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.FieldStamp
import java.time.OffsetDateTime

/**
 * The edits of a tag's scope (ADR 89; the PC's TagScopeEdits), each stamped so the newest version wins on every device. A tag has a scope
 * only while it is device-only; "make shared" deletes the scope, so a tag with no live scope is shared.
 */
object TagScopeEdits {
    /**
     * Makes [tag] device-only on [self] ([onlyOnThisDevice]), or shared. A device-only tag some other device already made joins that
     * device's list instead of replacing it. A device with no id can't be named: nothing changes. The scopes come back unchanged when the
     * tag already is as wanted.
     */
    fun setScope(scopes: List<SyncTagScope>?, tag: String, onlyOnThisDevice: Boolean, now: OffsetDateTime, self: String): List<SyncTagScope>? {
        val name = tag.trim()
        val me = self.lowercase()
        if (name.isEmpty() || (onlyOnThisDevice && me == FieldStamp.NOBODY)) return scopes

        val id = TaskScopes.tagIdFor(name)
        val list = scopes.orEmpty().toMutableList()
        val index = list.indexOfFirst { it.id.equals(id, ignoreCase = true) }
        val held = if (index >= 0) list[index] else null
        val live = held != null && held.deleted == null
        val stamp = nextStamp(now, held)

        val changed: SyncTagScope? = if (onlyOnThisDevice) {
            val devices = if (live) TaskScopes.normalized(held!!.onlyOn.orEmpty() + me) ?: listOf(me) else listOf(me)
            if (live && TaskRingDevices.same(held!!.onlyOn, devices)) null else SyncTagScope(id, if (live) held!!.name else name, devices, stamp)
        } else {
            if (live) held!!.deleted(stamp) else null
        }

        if (changed == null) return scopes
        if (index >= 0) list[index] = changed else list += changed
        return list.sortedBy { it.id.lowercase() }
    }

    // The edit time for a change to [held] (null: a new item): now, or one tick past the item's own newest time when that is later.
    private fun nextStamp(now: OffsetDateTime, held: SyncTagScope?): OffsetDateTime {
        if (held == null) return now
        val newest = if (held.deleted != null && DotNetTime.compare(held.deleted, held.edited) > 0) held.deleted else held.edited
        val after = DotNetTime.plusTick(newest)
        return if (DotNetTime.compare(after, now) > 0) after else now
    }
}
