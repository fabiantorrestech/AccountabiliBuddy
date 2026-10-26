package com.accountabilibuddy.companion.model

/**
 * One bedtime reminder (ADR 38): [minutesBefore] bedtime (0 = at bedtime), a notification or an [alarm], and the
 * devices that get it: [PHONE], [WATCH], [ALL_PCS] or "pc:<device id>". [sound] is "bell", "chime" or "rising".
 */
data class BedtimeReminder(
    val minutesBefore: Int,
    val alarm: Boolean,
    val devices: List<String>,
    val sound: String = "bell",
    val volume: Int = 80,
    val vibrate: Boolean = true,
) {
    companion object {
        const val PHONE = "phone"
        const val WATCH = "watch"
        const val ALL_PCS = "pcs"
    }
}

/** The bedtime reminders: on or off, and the list (shared sleep settings). */
data class BedtimeReminderSettings(val on: Boolean, val list: List<BedtimeReminder>) {
    companion object {
        /** 45 and 15 minutes before (notifications on the phone and the watch), and an alarm at bedtime on the phone. */
        val Default = BedtimeReminderSettings(
            true,
            listOf(
                BedtimeReminder(45, false, listOf(BedtimeReminder.PHONE, BedtimeReminder.WATCH)),
                BedtimeReminder(15, false, listOf(BedtimeReminder.PHONE, BedtimeReminder.WATCH)),
                BedtimeReminder(0, true, listOf(BedtimeReminder.PHONE)),
            ),
        )
    }
}
