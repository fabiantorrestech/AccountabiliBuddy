package com.accountabilibuddy.shared.sync

/**
 * The fixed vocabulary and bounds of sync receipts (ADR 86; the PC's `SyncReceiptParts`) and the clean-up applied as a file opens: a code
 * this app does not know is dropped, the lists are cut to their bounds, an entry for no device is dropped. Dropping never deletes data.
 */
object SyncReceiptParts {
    const val FOLDER = "folder"
    const val DRIVE = "drive"
    const val RELAY = "relay"
    const val QR = "qr"
    const val FILE = "file"

    /** The sync methods a receipt tells apart. */
    val METHODS: List<String> = listOf(FOLDER, DRIVE, RELAY, QR, FILE)

    /** The reasons a read attempt failed. */
    val FAILURES: List<String> = listOf("damaged", "otherGroup", "tooBig", "newerFormat")

    const val TAKEN = "taken"
    const val HELD = "held"
    const val OFF = "off"
    const val ABSENT = "absent"
    const val REFUSED = "refused"

    /** The outcomes of an accept, per part. */
    val STATES: List<String> = listOf(TAKEN, HELD, OFF, ABSENT, REFUSED)

    /** The parts a receipt reports on. */
    val CODES: List<String> = listOf("sessions", "dayPlan", "settings", "tasks", "oldLists", "goals", "goalHistory", "steps", "lunch", "actions")

    const val MAX_ENTRIES = 16
    const val MAX_METHODS = 5
    const val MAX_PARTS = 12
    const val MAX_MONTHS = 3

    /**
     * The receipts as a reader keeps them (the PC's `Clean`): no entry for no device or twice for one (the first stays), only known
     * methods, reasons, parts and states, a month only as "yyyy-MM" (the newest [MAX_MONTHS], newest first), every list within its bound.
     */
    fun clean(receipts: List<SyncReceipt>?): List<SyncReceipt>? {
        if (receipts == null) return null
        val seen = HashSet<java.util.UUID>()
        val clean = ArrayList<SyncReceipt>()
        for (receipt in receipts) {
            if (receipt.from == java.util.UUID(0L, 0L) || clean.size >= MAX_ENTRIES || !seen.add(receipt.from)) continue
            clean.add(
                receipt.copy(
                    byMethod = nonEmpty(receipt.byMethod?.filterKeys { it in METHODS }?.entries?.take(MAX_METHODS)?.associateTo(LinkedHashMap()) { it.key to it.value }),
                    failed = nonEmpty(receipt.failed?.filter { (key, value) -> key in METHODS && value in FAILURES }?.entries?.take(MAX_METHODS)?.associateTo(LinkedHashMap()) { it.key to it.value }),
                    parts = nonEmpty(receipt.parts?.filter { (key, value) -> key in CODES && value.state in STATES }?.entries?.take(MAX_PARTS)?.associateTo(LinkedHashMap()) { it.key to it.value }),
                    history = cleanMonths(receipt.history),
                ),
            )
        }

        return clean
    }

    /** Whether [month] is "yyyy-MM" with a real month. */
    fun isMonth(month: String?): Boolean {
        if (month == null || month.length != 7 || month[4] != '-') return false
        val digits = month.substring(0, 4) + month.substring(5)
        if (digits.any { it !in '0'..'9' }) return false
        return month.substring(0, 4).toInt() in 2000..9999 && month.substring(5).toInt() in 1..12
    }

    private fun cleanMonths(history: List<SyncHistoryReceipt>?): List<SyncHistoryReceipt>? {
        if (history == null) return null
        val months = history.filter { isMonth(it.month) }.distinctBy { it.month }.sortedByDescending { it.month }.take(MAX_MONTHS)
        return months.ifEmpty { null }
    }

    private fun <T> nonEmpty(items: LinkedHashMap<String, T>?): Map<String, T>? = items?.takeIf { it.isNotEmpty() }
}
