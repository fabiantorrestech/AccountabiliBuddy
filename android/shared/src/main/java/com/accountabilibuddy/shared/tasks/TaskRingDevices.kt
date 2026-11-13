package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.FieldStamp

/**
 * "Rings on" lists (the PC's TaskRingDevices): which of the user's devices ring for task reminders, as device ids. Two lists hold
 * one: the account's ([SyncTaskAccountSettings.ringOn]) and each label's ([TaskLabel.ringOn]). Null means every device, also one
 * added later; a list names the phones that ring (a PC always rings for its own reminders whatever a list says). A phone rings for a
 * task's reminder only when both lists name it ([ringsFor]).
 */
object TaskRingDevices {
    /** The longest list taken from a file or a backup. */
    const val MAX_DEVICES = 64

    /** Whether [device] is on the list (null: every device is). */
    fun rings(ringOn: List<String>?, device: String): Boolean = ringOn == null || ringOn.any { it.equals(device, ignoreCase = true) }

    /** Whether a device rings for a reminder of a task with the label's list: a PC always does; any other when both lists name it. */
    fun ringsFor(account: List<String>?, label: List<String>?, device: String, isPc: Boolean): Boolean =
        isPc || (rings(account, device) && rings(label, device))

    /** The list in its stored form: no empty id, each once, in id order (lower case); null stays null. */
    fun normalized(ringOn: List<String>?): List<String>? =
        ringOn?.map { it.lowercase() }?.filter { it != FieldStamp.NOBODY }?.distinct()?.sorted()

    /** Two lists ring on the same devices (null only equals null). */
    fun same(first: List<String>?, second: List<String>?): Boolean = text(first) == text(second)

    /**
     * The list with the phone [device] turned on or off. [phones] are the phones shown (never PCs); a list that then names all of
     * them is stored as null (every device, also one linked later). A device that isn't one of [phones] changes nothing.
     */
    fun toggled(ringOn: List<String>?, phones: List<String>, device: String, on: Boolean): List<String>? {
        val known = phones.map { it.lowercase() }
        val id = device.lowercase()
        if (id !in known) {
            return normalized(ringOn)
        }

        val devices = LinkedHashSet((ringOn ?: known).map { it.lowercase() })
        if (on) devices += id else devices -= id
        if (known.filter { it != FieldStamp.NOBODY }.all { it in devices }) {
            return null
        }

        return normalized(devices.toList())
    }

    /** The list as one text, to compare and to break merge ties: "*" for every device. */
    fun text(ringOn: List<String>?): String = if (ringOn == null) "*" else normalized(ringOn)!!.joinToString(",")
}
