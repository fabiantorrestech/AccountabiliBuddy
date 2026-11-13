package com.accountabilibuddy.shared.tasks

import kotlinx.serialization.json.JsonObject

/**
 * A task's number, shown as "142" (ADR 76; the PC's TaskNumber): final once the numbering device gave it; [temporary] when another
 * device did, until the final one arrives.
 */
data class TaskNumber(val value: Int, val temporary: Boolean = false, val extra: JsonObject = TaskUnknown.NONE)
