package com.accountabilibuddy.shared.sync

import java.time.OffsetDateTime
import java.util.UUID

/**
 * One entry of a sync file's `receipts` (ADR 86; the PC's `SyncReceipt`): what this device holds and applied of ONE other device's files.
 * Ids, times and fixed codes ([SyncReceiptParts]) only: never a name, title, file name, path or address. Also one sender's row of the
 * receiver-local record ([SyncReceiptsLocal]). The maps keep the order the codes were first put in (the PC writes them in that order).
 *
 * @property from The sender being acknowledged (never the writing device, never a removed device).
 * @property held The newest `writtenAt` held from [from], however it arrived (the card's `seen` value).
 * @property byMethod Per method: the newest `writtenAt` of [from]'s files that arrived by it.
 * @property failed Per method: why the last attempt on [from]'s file failed ([SyncReceiptParts.FAILURES]); absent after a success.
 * @property parts Per part ([SyncReceiptParts.CODES]): the outcome of the last accept of [from]'s file.
 * @property history The goal history month files merged from [from] (the newest [SyncReceiptParts.MAX_MONTHS]).
 */
data class SyncReceipt(
    val from: UUID,
    val held: OffsetDateTime,
    val byMethod: Map<String, OffsetDateTime>? = null,
    val failed: Map<String, String>? = null,
    val parts: Map<String, SyncPartReceipt>? = null,
    val history: List<SyncHistoryReceipt>? = null,
)

/** The outcome of the last accept of one part of a sender's file: [at] is the `writtenAt` of the file judged, [state] one of [SyncReceiptParts.STATES]. */
data class SyncPartReceipt(val at: OffsetDateTime, val state: String)

/** A goal history month file merged from a sender: [month] "yyyy-MM" and the [version]'s written time. */
data class SyncHistoryReceipt(val month: String, val version: OffsetDateTime)
