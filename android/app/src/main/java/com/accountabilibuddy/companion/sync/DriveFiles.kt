package com.accountabilibuddy.companion.sync

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.add
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import kotlinx.serialization.json.putJsonArray
import java.io.IOException
import java.net.URLEncoder
import java.time.Instant

/** A file in the app's Drive folder: its Drive id, name and last change. */
data class DriveFileInfo(val id: String, val name: String, val modifiedAt: Instant)

/**
 * A backup of a removed device in the app's Drive folder (ADR 55): `Backups/<device folder>/<date>.absync`.
 *
 * @property deviceName The name of the folder it is in: the removed device's name and the start of its id.
 * @property fileName E.g. "2026-10-28.absync".
 */
data class DriveBackupInfo(val id: String, val deviceName: String, val fileName: String, val modifiedAt: Instant, val size: Long)

/** Drive refused or couldn't be reached; [code] is the HTTP status (0: no answer). */
class DriveException(val code: Int, message: String) : IOException(message)

/**
 * The app's Drive folder over Drive v3 (ADR 26), the same one the PCs use: "AccountabiliBuddy sync", found among the
 * files the app made (drive.file), else made. Lists it, downloads a file, writes one. Plain Kotlin (the transport does
 * the network), so the checks run it against a fake.
 */
class DriveFiles(private val transport: DriveTransport, private val api: String = API, private val upload: String = UPLOAD) {
    private var folderId: String? = null

    fun list(token: String): List<DriveFileInfo> {
        val folder = folder(token)
        val files = mutableListOf<DriveFileInfo>()
        var page: String? = null
        do {
            val query = mutableMapOf(
                "q" to "'$folder' in parents and trashed=false",
                "fields" to "nextPageToken,files(id,name,modifiedTime)",
                "pageSize" to "100",
                "spaces" to "drive",
            )
            page?.let { query["pageToken"] = it }
            val json = json(transport.send("GET", url(api + "files", query), token, null, null))
            for (file in json["files"]?.jsonArray.orEmpty()) {
                val item = file.jsonObject
                files += DriveFileInfo(
                    item.getValue("id").jsonPrimitive.content,
                    item.getValue("name").jsonPrimitive.content,
                    Instant.parse(item.getValue("modifiedTime").jsonPrimitive.content),
                )
            }
            page = json["nextPageToken"]?.jsonPrimitive?.content
        } while (page != null)

        return files
    }

    fun download(token: String, id: String): ByteArray {
        val response = transport.send("GET", url(api + "files/" + encode(id), mapOf("alt" to "media")), token, null, null)
        if (!response.isSuccess) {
            throw DriveException(response.code, "Drive said ${response.code}.")
        }

        return response.body
    }

    /**
     * Writes [name]: a new file when [existingId] is null, else that file's new content. Returns its Drive id. A new file goes in the
     * app's folder, or in [parent] (a folder's Drive id) when given.
     */
    fun upload(token: String, name: String, content: ByteArray, existingId: String?, parent: String? = null): String {
        if (existingId != null) {
            val updated = transport.send(
                "PATCH", url(upload + "files/" + encode(existingId), mapOf("uploadType" to "media", "fields" to "id")), token, MIME, content,
            )
            return json(updated).getValue("id").jsonPrimitive.content
        }

        val metadata = buildJsonObject {
            put("name", name)
            putJsonArray("parents") { add(parent ?: folder(token)) }
        }.toString()
        val boundary = "ab" + System.nanoTime()
        val body = buildString {
            append("--$boundary\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n$metadata\r\n")
            append("--$boundary\r\nContent-Type: $MIME\r\n\r\n")
        }.toByteArray() + content + "\r\n--$boundary--\r\n".toByteArray()
        val created = transport.send(
            "POST", url(upload + "files", mapOf("uploadType" to "multipart", "fields" to "id")), token,
            "multipart/related; boundary=$boundary", body,
        )
        return json(created).getValue("id").jsonPrimitive.content
    }

    /**
     * Writes a removed device's backup (ADR 55) to `Backups/[deviceFolder]/[fileName]` inside the app's folder, making the folders
     * when needed; a file of the same name (the same day) is replaced. Returns its Drive id.
     */
    fun uploadBackup(token: String, deviceFolder: String, fileName: String, content: ByteArray): String {
        val backups = childFolder(token, folder(token), BACKUPS_FOLDER_NAME, create = true)!!
        val device = childFolder(token, backups, deviceFolder, create = true)!!
        val existing = pages(token, "'$device' in parents and trashed=false and mimeType!='$FOLDER_MIME'", "nextPageToken,files(id,name)")
            .firstOrNull { it.getValue("name").jsonPrimitive.content == fileName }?.getValue("id")?.jsonPrimitive?.content
        return upload(token, fileName, content, existing, device)
    }

    /**
     * ADR 71: a sealed settings backup into `Settings backups/[kind]/[deviceFolder]/` in the app's folder (the PC's layout); only the
     * newest [keep] files there stay.
     */
    fun uploadSettingsBackup(token: String, kind: String, deviceFolder: String, fileName: String, content: ByteArray, keep: Int) {
        val root = childFolder(token, folder(token), SETTINGS_BACKUPS_FOLDER_NAME, create = true)!!
        val kindFolder = childFolder(token, root, kind, create = true)!!
        val device = childFolder(token, kindFolder, deviceFolder, create = true)!!
        upload(token, fileName, content, null, device)
        pages(token, "'$device' in parents and trashed=false and mimeType!='$FOLDER_MIME'", "nextPageToken,files(id,name,modifiedTime)")
            .sortedByDescending { it.getValue("modifiedTime").jsonPrimitive.content }
            .drop(keep)
            .forEach { delete(token, it.getValue("id").jsonPrimitive.content) }
    }

    /** ADR 71: every device's sealed settings backups of [kind], newest first; empty when there is none. */
    fun listSettingsBackups(token: String, kind: String): List<DriveBackupInfo> {
        val root = childFolder(token, folder(token), SETTINGS_BACKUPS_FOLDER_NAME, create = false) ?: return emptyList()
        val kindFolder = childFolder(token, root, kind, create = false) ?: return emptyList()
        val found = mutableListOf<DriveBackupInfo>()
        for (device in pages(token, "'$kindFolder' in parents and trashed=false and mimeType='$FOLDER_MIME'", "nextPageToken,files(id,name)")) {
            val deviceName = device.getValue("name").jsonPrimitive.content
            val deviceId = device.getValue("id").jsonPrimitive.content
            for (file in pages(token, "'$deviceId' in parents and trashed=false and mimeType!='$FOLDER_MIME'", "nextPageToken,files(id,name,modifiedTime,size)")) {
                found += DriveBackupInfo(
                    file.getValue("id").jsonPrimitive.content, deviceName, file.getValue("name").jsonPrimitive.content,
                    Instant.parse(file.getValue("modifiedTime").jsonPrimitive.content),
                    file["size"]?.jsonPrimitive?.content?.toLongOrNull() ?: 0L,
                )
            }
        }

        return found.sortedByDescending { it.modifiedAt }
    }

    /** The backups under `Backups/` in the app's folder (every device's folder, every `.absync` file); empty when there is none. */
    fun listBackups(token: String): List<DriveBackupInfo> {
        val backups = childFolder(token, folder(token), BACKUPS_FOLDER_NAME, create = false) ?: return emptyList()
        val found = mutableListOf<DriveBackupInfo>()
        for (device in pages(token, "'$backups' in parents and trashed=false and mimeType='$FOLDER_MIME'", "nextPageToken,files(id,name)")) {
            val deviceName = device.getValue("name").jsonPrimitive.content
            val deviceId = device.getValue("id").jsonPrimitive.content
            val inside = pages(
                token, "'$deviceId' in parents and trashed=false and mimeType!='$FOLDER_MIME'",
                "nextPageToken,files(id,name,modifiedTime,size)",
            )
            for (file in inside) {
                val name = file.getValue("name").jsonPrimitive.content
                if (name.endsWith(".absync", ignoreCase = true)) {
                    found += DriveBackupInfo(
                        file.getValue("id").jsonPrimitive.content, deviceName, name,
                        Instant.parse(file.getValue("modifiedTime").jsonPrimitive.content),
                        file["size"]?.jsonPrimitive?.content?.toLongOrNull() ?: 0L,
                    )
                }
            }
        }

        return found
    }

    // A folder directly inside [parent], by name; made when [create] and it isn't there (null when it isn't there and [create] is false).
    private fun childFolder(token: String, parent: String, name: String, create: Boolean): String? {
        val found = pages(token, "name='${escapeQuery(name)}' and mimeType='$FOLDER_MIME' and '$parent' in parents and trashed=false", "files(id)")
        found.firstOrNull()?.getValue("id")?.jsonPrimitive?.content?.let { return it }
        if (!create) {
            return null
        }

        val body = buildJsonObject {
            put("name", name)
            put("mimeType", FOLDER_MIME)
            putJsonArray("parents") { add(parent) }
        }.toString().toByteArray()
        val created = json(transport.send("POST", url(api + "files", mapOf("fields" to "id")), token, "application/json; charset=UTF-8", body))
        return created.getValue("id").jsonPrimitive.content
    }

    // Every file a query finds, page after page (each element is one file with the requested [fields]).
    private fun pages(token: String, query: String, fields: String): List<JsonObject> {
        val files = mutableListOf<JsonObject>()
        var page: String? = null
        do {
            val parameters = mutableMapOf("q" to query, "fields" to fields, "pageSize" to "100", "spaces" to "drive")
            page?.let { parameters["pageToken"] = it }
            val json = json(transport.send("GET", url(api + "files", parameters), token, null, null))
            json["files"]?.jsonArray.orEmpty().forEach { files += it.jsonObject }
            page = json["nextPageToken"]?.jsonPrimitive?.content
        } while (page != null)

        return files
    }

    // A name inside a Drive query string: a backslash and a single quote are escaped.
    private fun escapeQuery(text: String): String = text.replace("\\", "\\\\").replace("'", "\\'")

    /** Deletes the file with Drive id [id] (one the app made); a file that is already gone is fine. */
    fun delete(token: String, id: String) {
        val response = transport.send("DELETE", url(api + "files/" + encode(id), emptyMap()), token, null, null)
        if (!response.isSuccess && response.code != 404) {
            throw DriveException(response.code, "Drive said ${response.code}.")
        }
    }

    private fun folder(token: String): String {
        folderId?.let { return it }
        val found = json(
            transport.send(
                "GET",
                url(api + "files", mapOf("q" to "name='$FOLDER_NAME' and mimeType='$FOLDER_MIME' and trashed=false", "fields" to "files(id)", "spaces" to "drive")),
                token, null, null,
            ),
        )
        (found["files"] as? JsonArray)?.firstOrNull()?.jsonObject?.get("id")?.jsonPrimitive?.content?.let {
            folderId = it
            return it
        }

        val body = buildJsonObject {
            put("name", FOLDER_NAME)
            put("mimeType", FOLDER_MIME)
        }.toString().toByteArray()
        val created = json(transport.send("POST", url(api + "files", mapOf("fields" to "id")), token, "application/json; charset=UTF-8", body))
        return created.getValue("id").jsonPrimitive.content.also { folderId = it }
    }

    private fun json(response: DriveResponse): JsonObject {
        if (!response.isSuccess) {
            throw DriveException(response.code, "Drive said ${response.code}.")
        }

        return Json.parseToJsonElement(response.body.decodeToString().ifEmpty { "{}" }).jsonObject
    }

    private fun url(base: String, query: Map<String, String>): String =
        base + "?" + query.entries.joinToString("&") { (key, value) -> "${encode(key)}=${encode(value)}" }

    private fun encode(text: String): String = URLEncoder.encode(text, "UTF-8").replace("+", "%20")

    companion object {
        /** The same folder name as the PC's (App's GoogleDriveFiles.FolderName). */
        const val FOLDER_NAME = "AccountabiliBuddy sync"
        private const val FOLDER_MIME = "application/vnd.google-apps.folder"

        /** The folder inside the app's folder that holds the removed devices' backups, one folder per device (ADR 55), as on the PC. */
        const val BACKUPS_FOLDER_NAME = "Backups"

        /** ADR 71: the folder of the sealed settings backups, apart from the removed devices' [BACKUPS_FOLDER_NAME]. */
        const val SETTINGS_BACKUPS_FOLDER_NAME = "Settings backups"
        private const val MIME = "application/octet-stream"
        private const val API = "https://www.googleapis.com/drive/v3/"
        private const val UPLOAD = "https://www.googleapis.com/upload/drive/v3/"
    }
}
