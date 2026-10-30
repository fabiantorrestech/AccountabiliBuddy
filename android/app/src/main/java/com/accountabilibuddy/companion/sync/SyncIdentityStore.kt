package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.companion.data.JsonValues.string
import com.accountabilibuddy.shared.TokenVault
import com.accountabilibuddy.shared.sync.SyncDeviceKey
import com.accountabilibuddy.shared.sync.SyncKey
import com.accountabilibuddy.shared.sync.SyncKeyCode
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.io.File
import java.util.UUID

/**
 * This phone in the device group (ADR 22), kept in sync.json in the app's private folder: its sync device id (made
 * once, kept when leaving a group) and the group's link code, sealed by [TokenVault]. The key is made from the code.
 */
class SyncIdentityStore(private val file: File) {
    private val _key = MutableStateFlow<SyncKey?>(null)
    private var sealedCode: String? = null
    private var sealedDeviceKey: String? = null

    /** This phone's id in every sync file it writes. */
    val deviceId: UUID

    /** This phone's key pair for receiving a new link code when the group is re-keyed (ADR 54); made once, kept for good. */
    val deviceKey: SyncDeviceKey

    /** The group's key, or null while the phone isn't in a group. */
    val key: StateFlow<SyncKey?> = _key.asStateFlow()

    init {
        val json = runCatching { Json.parseToJsonElement(file.readText()) as JsonObject }.getOrNull()
        deviceId = json?.string("deviceId")?.let { runCatching { UUID.fromString(it) }.getOrNull() } ?: UUID.randomUUID()
        sealedCode = json?.string("sealedCode")
        _key.value = code()?.let(SyncKeyCode::parse)
        sealedDeviceKey = json?.string("sealedDeviceKey")
        // A key pair that can't be opened (the Keystore key went with the app's data) is made again.
        val restored = openDeviceKey()
        deviceKey = restored ?: SyncDeviceKey.generate()
        if (json == null || restored == null) {
            if (restored == null) {
                sealedDeviceKey = sealDeviceKey(deviceKey)
            }

            save()
        }
    }

    // "<PKCS#8 private key>.<public key>", both base64, sealed by the Keystore.
    private fun sealDeviceKey(key: SyncDeviceKey): String {
        val encoder = java.util.Base64.getEncoder()
        return TokenVault.seal(encoder.encodeToString(key.pkcs8()) + "." + encoder.encodeToString(key.publicKey), TokenVault.DEVICE_KEY_ALIAS)
    }

    private fun openDeviceKey(): SyncDeviceKey? {
        val parts = sealedDeviceKey?.let { TokenVault.open(it, TokenVault.DEVICE_KEY_ALIAS) }?.split('.') ?: return null
        if (parts.size != 2) {
            return null
        }

        return runCatching {
            val decoder = java.util.Base64.getDecoder()
            SyncDeviceKey.restore(decoder.decode(parts[0]), decoder.decode(parts[1]))
        }.getOrNull()
    }

    /** The group's link code (40 characters), to show on this phone so another device can join; null outside a group. */
    @Synchronized
    fun code(): String? = sealedCode?.let(TokenVault::open)

    /** Joins the group of [code]; false (changing nothing) when it isn't a valid code. */
    @Synchronized
    fun join(code: String): Boolean {
        val key = SyncKeyCode.parse(code) ?: return false
        sealedCode = TokenVault.seal(code.trim())
        save()
        _key.value = key
        return true
    }

    /** Leaves the group; the device id stays. */
    @Synchronized
    fun leave() {
        sealedCode = null
        save()
        _key.value = null
    }

    private fun save() {
        val json = buildJsonObject {
            put("deviceId", deviceId.toString())
            sealedCode?.let { put("sealedCode", it) }
            sealedDeviceKey?.let { put("sealedDeviceKey", it) }
        }
        file.parentFile?.mkdirs()
        val temporary = File(file.parentFile, "${file.name}.tmp")
        temporary.writeText(json.toString())
        if (!temporary.renameTo(file)) {
            file.delete()
            temporary.renameTo(file)
        }
    }
}
