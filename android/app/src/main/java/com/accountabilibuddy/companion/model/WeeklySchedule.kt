package com.accountabilibuddy.companion.model

/** Weekly blocked time, plus [oneOffBlocks] on single dates, minus [timeOff]. */
data class WeeklySchedule(
    val blocks: List<ScheduleBlock> = emptyList(),
    val timeOff: List<TimeOff> = emptyList(),
    val oneOffBlocks: List<OneOffBlock> = emptyList(),
)
