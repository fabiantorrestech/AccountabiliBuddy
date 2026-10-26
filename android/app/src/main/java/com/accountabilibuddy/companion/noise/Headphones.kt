package com.accountabilibuddy.companion.noise

import android.media.AudioDeviceInfo
import android.media.AudioManager
import android.os.Build

/**
 * Whether any headphones are connected, for the noise player's lock and "Stop when headphones disconnect": a wired
 * or USB headset, or any Bluetooth audio output (A2DP, headset, LE audio). Independent of the devices chosen for
 * "Play when headphones connect".
 */
object Headphones {
    private val WIRED = setOf(AudioDeviceInfo.TYPE_WIRED_HEADPHONES, AudioDeviceInfo.TYPE_WIRED_HEADSET, AudioDeviceInfo.TYPE_USB_HEADSET)
    private val BLUETOOTH = setOf(AudioDeviceInfo.TYPE_BLUETOOTH_A2DP, AudioDeviceInfo.TYPE_BLUETOOTH_SCO)

    /** True when an output in Android's device list is headphones, leaving out the ids in [ignoring] (outputs just removed). */
    fun connected(audio: AudioManager, ignoring: Set<Int> = emptySet()): Boolean =
        audio.getDevices(AudioManager.GET_DEVICES_OUTPUTS).any { it.id !in ignoring && isHeadphones(it) }

    fun isHeadphones(device: AudioDeviceInfo): Boolean =
        device.isSink && (device.type in WIRED || device.type in BLUETOOTH || isLeAudio(device))

    private fun isLeAudio(device: AudioDeviceInfo): Boolean =
        Build.VERSION.SDK_INT >= Build.VERSION_CODES.S && device.type == AudioDeviceInfo.TYPE_BLE_HEADSET
}
