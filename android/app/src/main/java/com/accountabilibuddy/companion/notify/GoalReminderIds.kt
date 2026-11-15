package com.accountabilibuddy.companion.notify

/**
 * The ids of a goal reminder's notification and of its four pending intents (open, Log, Tick today, Snooze), FX-A (R6b3 review NIT).
 * The notification id is a base plus a bucket of 2^20 of the occurrence's key; its four request codes are four consecutive numbers in a
 * range of their own, so two intents of one reminder never share a code, and nothing here meets the review's (7000, 7001) or the alarm's.
 * Pure (checked on its own).
 */
object GoalReminderIds {
    /** The weekly review's notification and the request code of its Snooze button. */
    const val REVIEW_ID = 7000
    const val REVIEW_SNOOZE_CODE = REVIEW_ID + 1

    private const val NOTIFICATION_BASE = 1_000_000
    private const val BUCKETS = 1 shl 20
    private const val CODE_BASE = 0x40000000

    enum class Family(val offset: Int) { Open(0), Log(1), Tick(2), Snooze(3) }

    fun bucket(key: String): Int = key.hashCode() and (BUCKETS - 1)

    fun notificationId(key: String): Int = NOTIFICATION_BASE + bucket(key)

    fun code(key: String, family: Family): Int = CODE_BASE + (bucket(key) shl 2) + family.offset
}
