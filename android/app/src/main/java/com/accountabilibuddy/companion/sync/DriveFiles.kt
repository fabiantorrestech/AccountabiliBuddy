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

    /** Writes [name]: a new file when [existingId] is null, else that file's new content. Returns its Drive id. */
    fun upload(token: String, name: String, content: ByteArray, existingId: String?): String {
        if (existingId != null) {
            val updated = transport.send(
                "PATCH", url(upload + "files/" + encode(existingId), mapOf("uploadType" to "media", "fields" to "id")), token, MIME, content,
            )
            return json(updated).getValue("id").jsonPrimitive.content
        }

        val metadata = buildJsonObject {
            put("name", name)
            putJsonArray("parents") { add(folder(token)) }
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

    private fun encode(text: String): String = URLEncoder.encode(text, Charsets.UTF_8).replace("+", "%20")

    companion object {
        /** The same folder name as the PC's (App's GoogleDriveFiles.FolderName). */
        const val FOLDER_NAME = "AccountabiliBuddy sync"
        private const val FOLDER_MIME = "application/vnd.google-apps.folder"
        private const val MIME = "application/octet-stream"
        private const val API = "https://www.googleapis.com/drive/v3/"
        private const val UPLOAD = "https://www.googleapis.com/upload/drive/v3/"
    }
}
