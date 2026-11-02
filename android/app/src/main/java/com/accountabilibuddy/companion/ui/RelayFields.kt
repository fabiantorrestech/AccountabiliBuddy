package com.accountabilibuddy.companion.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import com.accountabilibuddy.companion.sync.PhoneSyncSettings
import com.accountabilibuddy.companion.sync.RelayAddress

/**
 * What is typed into the relay card and the checks on it. One holder so the card and the "Own relay" switch share the same typing:
 * the switch turns the relay on only with fields that pass [checked], which says why not in [error].
 */
class RelayFields(settings: PhoneSyncSettings) {
    var address by mutableStateOf(settings.relayUrl.orEmpty())
    var pin by mutableStateOf(settings.relayPin.orEmpty())
    var admission by mutableStateOf(settings.relayAdmission.orEmpty())
    var error by mutableStateOf<String?>(null)

    /** The settings with these fields in them, or null with the reason in [error]. */
    fun checked(settings: PhoneSyncSettings): PhoneSyncSettings? {
        val url = RelayAddress.normalize(address)
        if (url == null) {
            error = "Type the relay's address starting with https://, e.g. https://192.168.1.20:8743."
            return null
        }

        val typedPin = pin.trim()
        if (typedPin.isNotEmpty() && !RelayAddress.isPin(typedPin)) {
            error = "The pin is 43 letters, digits, - and _ (leave it empty for a relay with a normal certificate)."
            return null
        }

        val typedAdmission = admission.trim()
        if (typedAdmission.isNotEmpty() && !RelayAddress.isAdmissionKey(typedAdmission)) {
            error = "The admission key is 43 letters, digits, - and _ (leave it empty if the relay has none)."
            return null
        }

        error = null
        return settings.copy(relayUrl = url, relayPin = typedPin.ifEmpty { null }, relayAdmission = typedAdmission.ifEmpty { null })
    }

    /** The fields differ from what is saved. */
    fun changed(settings: PhoneSyncSettings): Boolean =
        RelayAddress.normalize(address) != settings.relayUrl || pin.trim().ifEmpty { null } != settings.relayPin ||
            admission.trim().ifEmpty { null } != settings.relayAdmission
}

/** The relay fields, started from the saved settings and started again when those change (the old card's `remember(settings.relayUrl)` keys). */
@Composable
internal fun rememberRelayFields(settings: PhoneSyncSettings): RelayFields =
    remember(settings.relayUrl, settings.relayPin, settings.relayAdmission) { RelayFields(settings) }
