package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncFile
import java.time.Instant

/**
 * The phone's copy of another device's part (ADR 22), as on the PC (App's ReceivedDevice): every file received from it
 * merged into one.
 *
 * @property receivedAt When the newest file from it arrived.
 * @property method How the newest file came ([com.accountabilibuddy.shared.sync.SyncDeviceCard.METHOD_LINK], QR, folder
 * or file); the phone never carries a file back the way it came ([SyncCarry.filesFor]).
 * @property sealed The newest file's sealed bytes exactly as they arrived: what the phone carries on. A merged copy is
 * never sealed again and relayed, since it has gaps where its files didn't meet.
 */
class ReceivedDevice(val file: SyncFile, val receivedAt: Instant, val method: String, val sealed: ByteArray)
