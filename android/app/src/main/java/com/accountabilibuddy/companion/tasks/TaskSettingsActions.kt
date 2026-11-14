package com.accountabilibuddy.companion.tasks

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.shared.sync.SyncDeviceCard
import com.accountabilibuddy.shared.tasks.TaskAccountSettingsEdits
import com.accountabilibuddy.shared.tasks.TaskLabelEdits
import com.accountabilibuddy.shared.tasks.TaskLabelNameCheck
import com.accountabilibuddy.shared.tasks.TaskLabelColors
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskScopeRules
import com.accountabilibuddy.shared.tasks.TaskRingDevices

/** A phone that can be on a "Rings on" list: this one, or another phone of the group (PCs always ring for their own reminders). */
data class RingPhone(val id: String, val name: String, val isThisPhone: Boolean)

/**
 * What the phone's Settings › Tasks, Labels and Alerts › Task reminders write (R7.4). Every change goes through the one task list
 * ([PhoneTaskStore.change]: stamped by this phone, normalized, kept) and the rules in the shared package
 * ([TaskAccountSettingsEdits], [TaskLabelEdits]); the sync is told so the change goes out in this phone's next file.
 */
class TaskSettingsActions(private val app: CompanionApp) {
    private val store get() = app.taskModel

    /** The phones a "Rings on" list can name: this one first, then the group's other phones still in it, by name. */
    fun phones(): List<RingPhone> {
        val self = app.sync.deviceId.toString().lowercase()
        val others = app.sync.received.value.map { it.file.device }
            .filter { it.kind == SyncDeviceCard.KIND_PHONE && it.deviceId.toString().lowercase() != self }
            .map { RingPhone(it.deviceId.toString().lowercase(), it.name, false) }
            .sortedBy { it.name.lowercase() }
        return listOf(RingPhone(self, "This phone", true)) + others
    }

    /** The PCs of the group still in it (they always ring for their own reminders), by name. */
    fun pcNames(): List<String> = app.sync.received.value.map { it.file.device }.filter { it.kind == SyncDeviceCard.KIND_PC }.map { it.name }.sortedBy { it.lowercase() }

    /** Turns [phone] on or off the account's "Rings on" list ([TaskRingDevices.toggled]). */
    fun setAccountRing(phone: String, on: Boolean) {
        val phones = phones().map { it.id }
        edit { lists, self, now -> lists.copy(account = TaskAccountSettingsEdits.withRingOn(lists.account, TaskRingDevices.toggled(lists.account?.ringOn, phones, phone, on), now, self)) }
    }

    /** Turns [phone] on or off the label's "Rings on" list; a list naming every phone is stored as "every device". */
    fun setLabelRing(labelId: String, phone: String, on: Boolean) {
        val phones = phones().map { it.id }
        edit { lists, self, now ->
            val label = lists.labels.map { it.label }.firstOrNull { it.isLive && it.id.equals(labelId, ignoreCase = true) } ?: return@edit lists
            TaskLabelEdits.setRingOn(lists, label.id, TaskRingDevices.toggled(label.ringOn, phones, phone, on), now, self)
        }
    }

    /** The view the Tasks page opens with on every device (null: the built-in Default). */
    fun setDefaultView(viewId: String?) {
        edit { lists, self, now -> lists.copy(account = TaskAccountSettingsEdits.withDefaultView(lists.account, viewId, now, self)) }
    }

    /** A new label; false when the name can't be used (blank or taken). [color] is what was typed or picked (stored form). */
    fun addLabel(name: String, color: String?, onlyHere: Boolean = false, makeShared: Boolean = false): Boolean {
        if (TaskLabelEdits.checkName(store.lists.value, null, name, selfId(), store.removed()) != TaskLabelNameCheck.Fine) return false
        edit { lists, self, now ->
            val added = TaskLabelEdits.add(lists, name, color, now, self, onlyOnThisDevice = onlyHere, removed = store.removed())
            // "Make shared" is the explicit act (the screen asked first); the add itself only ever joins a name other devices hold.
            val named = TaskLabelEdits.named(added, name)
            if (makeShared && named != null) TaskScopeRules.makeLabelShared(added, named.id, confirmed = true, now = now, self = self) else added
        }
        return true
    }

    /** A label's new name and colour; false when the name can't be used. */
    fun changeLabel(labelId: String, name: String, color: String): Boolean {
        val lists = store.lists.value
        val label = lists.labels.map { it.label }.firstOrNull { it.isLive && it.id.equals(labelId, ignoreCase = true) } ?: return false
        if (TaskLabelEdits.checkName(lists, labelId, name, selfId(), store.removed()) != TaskLabelNameCheck.Fine) return false
        edit { held, self, now ->
            val renamed = TaskLabelEdits.rename(held, label.name, name, now, self, store.removed())
            if (TaskLabelColors.isValid(color)) TaskLabelEdits.recolour(renamed, name.trim(), color, now, self, store.removed()) else renamed
        }
        return true
    }

    private fun selfId(): String = app.sync.deviceId.toString().lowercase()

    /** ADR 89: the label shown only on the devices that hold it, this phone among them (a shared label becomes this phone's own). Display only: applies at once. */
    fun setLabelOnlyHere(labelId: String) = edit { lists, self, now -> TaskScopeRules.makeLabelDeviceOnly(lists, labelId, now, self) }

    /** ADR 89: "Make shared": every device shows the label. Only a [confirmed] call changes anything (the screen asks "It will show on all your devices" first). */
    fun makeLabelShared(labelId: String, confirmed: Boolean) = edit { lists, self, now -> TaskScopeRules.makeLabelShared(lists, labelId, confirmed, now, self) }

    /** ADR 89: the tag shown only on this phone (and the devices that hold it). */
    fun setTagOnlyHere(tag: String) = edit { lists, self, now -> TaskScopeRules.makeTagDeviceOnly(lists, tag, now, self) }

    /** ADR 89: "Make shared" for a tag; only a [confirmed] call changes anything. */
    fun makeTagShared(tag: String, confirmed: Boolean) = edit { lists, self, now -> TaskScopeRules.makeTagShared(lists, tag, confirmed, now, self) }

    /** Deletes the label: its tasks are left with No label. */
    fun deleteLabel(labelId: String) {
        val label = store.lists.value.labels.map { it.label }.firstOrNull { it.isLive && it.id.equals(labelId, ignoreCase = true) } ?: return
        edit { lists, self, now -> TaskLabelEdits.delete(lists, label.name, now, self, store.removed()) }
    }

    private fun edit(change: (TaskLists, String, java.time.OffsetDateTime) -> TaskLists) {
        if (!store.isActive) return
        if (store.change(change)) app.sharedLists.onChanged()
    }
}
