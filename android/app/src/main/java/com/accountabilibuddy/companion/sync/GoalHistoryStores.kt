package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.shared.goals.GoalHistoryNames
import com.accountabilibuddy.shared.goals.GoalHistoryRemoteFile
import com.accountabilibuddy.shared.goals.GoalHistoryStore
import java.time.OffsetDateTime
import java.time.ZoneOffset

/** The goal history files in the app's own Drive folder (ADR 26, drive.file scope), next to the sync files, under their own extension (the PC's `DriveGoalHistoryStore`). */
class DriveGoalHistoryStore(private val files: DriveFiles, private val token: String) : GoalHistoryStore {
    private val ids = HashMap<String, String>()

    override val transport = "drive"
    override val extension = GoalHistoryNames.DRIVE_EXTENSION
    override val maxFileBytes = 16L * 1024 * 1024
    override val maxFiles: Int? = null
    override val expires = false
    override val canDelete = true

    override fun list(): List<GoalHistoryRemoteFile> {
        val listed = files.list(token)
        ids.clear()
        listed.forEach { ids[it.name] = it.id }
        return listed.map { GoalHistoryRemoteFile(it.name, OffsetDateTime.ofInstant(it.modifiedAt, ZoneOffset.UTC), it.size, null) }
    }

    override fun get(name: String): ByteArray? = ids[name]?.let { files.download(token, it) }

    override fun put(name: String, bytes: ByteArray) {
        ids[name] = files.upload(token, name, bytes, ids[name])
    }

    override fun delete(name: String): Boolean {
        ids[name]?.let {
            files.delete(token, it)
            ids.remove(name)
        }

        return true
    }
}

/**
 * The goal history files on the user's own relay (ADR 46): names of 12 hex characters plus ".absync", at most 64 files and 4 MiB a file, files
 * untouched for 30 days deleted, and a device deletes its files there only when it leaves the group. So the files are put again every 25 days
 * and a rolled-up month file is simply not put again (it expires).
 */
class RelayGoalHistoryStore(private val endpoint: RelayEndpoint) : GoalHistoryStore {
    override val transport = "relay"
    override val extension = GoalHistoryNames.RELAY_EXTENSION
    override val maxFileBytes = 4L * 1024 * 1024
    override val maxFiles: Int? = 64
    override val expires = true
    override val canDelete = false

    override fun list(): List<GoalHistoryRemoteFile> =
        RelayNetwork.list(endpoint).map { GoalHistoryRemoteFile(it.name, it.at ?: OffsetDateTime.of(1, 1, 1, 0, 0, 0, 0, ZoneOffset.UTC), it.size, it.etag) }

    override fun get(name: String): ByteArray? = RelayNetwork.get(endpoint, name)?.bytes

    override fun put(name: String, bytes: ByteArray) {
        RelayNetwork.put(endpoint, name, bytes)
    }

    override fun delete(name: String): Boolean = false
}
