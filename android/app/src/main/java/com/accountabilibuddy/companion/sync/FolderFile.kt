package com.accountabilibuddy.companion.sync

import java.time.Instant
import java.util.UUID

/** A sealed sync file to write to the sync folder under [name]: whose it is and when that device wrote it ([own]: this phone's own file). */
class FolderFile(val name: String, val deviceId: UUID, val writtenAt: Instant, val sealed: ByteArray, val own: Boolean = false)
