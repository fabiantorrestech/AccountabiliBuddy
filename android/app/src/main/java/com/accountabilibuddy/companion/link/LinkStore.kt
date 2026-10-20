package com.accountabilibuddy.companion.link

import com.accountabilibuddy.companion.data.JsonValues.int
import com.accountabilibuddy.companion.data.JsonValues.string
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.io.File

/** The paired PC (link.json in the app's private folder); the token inside is sealed by [TokenVault]. */
class LinkStore(private val file: File) {
    private val _pc = MutableStateFlow(load())

    val pc: StateFlow<LinkedPc?> = _pc.asStateFlow()

    @Synchronized
    fun save(pc: LinkedPc) {
        val json = buildJsonObject {
            put("pcName", pc.pcName)
            put("host", pc.host)
            put("port", pc.port)
            put("fingerprint", pc.fingerprint)
            put("deviceId", pc.deviceId)
            put("sealedToken", pc.sealedToken)
        }
        file.parentFile?.mkdirs()
        val temporary = File(file.parentFile, "${file.name}.tmp")
        temporary.writeText(json.toString())
        if (!temporary.renameTo(file)) {
            file.delete()
            temporary.renameTo(file)
        }

        _pc.value = pc
    }

    @Synchronized
    fun clear() {
        file.delete()
        _pc.value = null
    }

    private fun load(): LinkedPc? {
        if (!file.exists()) {
            return null
        }

        return runCatching {
            val json = Json.parseToJsonElement(file.readText()) as JsonObject
            LinkedPc(
                pcName = json.string("pcName") ?: "PC",
                host = json.string("host")!!,
                port = json.int("port")!!,
                fingerprint = json.string("fingerprint")!!,
                deviceId = json.string("deviceId")!!,
                sealedToken = json.string("sealedToken")!!,
            )
        }.getOrNull()
    }
}
