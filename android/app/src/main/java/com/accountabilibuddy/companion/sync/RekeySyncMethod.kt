package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.sync.SyncKey
import java.util.UUID

/** A sync method that can carry the rekey file (ADR 54) and clear a group's old files: the sync folder, Drive and the relay. */
interface RekeySyncMethod {
    /** The method's name on the card, e.g. [com.accountabilibuddy.shared.sync.SyncDeviceCard.METHOD_DRIVE]. */
    val method: String

    /**
     * Writes the rekey file where this method keeps the group's files, for the group of [key] (the key from before the switch:
     * the devices that haven't switched yet still listen there).
     *
     * @return Null when this method isn't in use; true when the file was written; false when writing failed.
     */
    suspend fun publishRekey(sealed: ByteArray, fileName: String, key: SyncKey): Boolean?

    /**
     * After the group got a new key: deletes the files of [deviceIds] (this phone's own and the removed devices') from the old
     * group [oldKey]. Best effort; the rekey file stays, since a device that is behind (or the removed one) still has to read it.
     */
    suspend fun clearOldGroup(oldKey: SyncKey, deviceIds: List<UUID>)
}
