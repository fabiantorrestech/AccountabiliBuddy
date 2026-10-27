package com.accountabilibuddy.companion.noise

import android.annotation.SuppressLint
import android.bluetooth.BluetoothClass
import android.bluetooth.BluetoothManager
import android.content.Context
import android.media.AudioDeviceCallback
import android.media.AudioDeviceInfo
import android.media.AudioManager
import android.os.Build
import android.os.Handler
import android.os.Looper
import com.accountabilibuddy.companion.pause.CarReceiver

/**
 * Plays the noise player's last file when chosen headphones connect (any time of day) and pauses it when they go,
 * so it never carries on through the speaker. Watches Android's audio outputs, which appear once sound can go to
 * them; outputs already there when it starts aren't counted. Runs while the app's process lives (the pinned timeline
 * keeps it alive).
 */
class HeadphoneWatcher(
    private val context: Context,
    private val store: HeadphoneStore,
    private val player: NoisePlayer,
    /** False when "Only at certain times" rules out playing now. The player's headphone lock still applies in [NoisePlayer.play]. */
    private val schedulePermits: () -> Boolean = { true },
) {
    private val audio = context.getSystemService(AudioManager::class.java)
    private val handler = Handler(Looper.getMainLooper())
    private val known = mutableSetOf<Int>()
    private val started = mutableSetOf<Int>()

    private val callback = object : AudioDeviceCallback() {
        override fun onAudioDevicesAdded(addedDevices: Array<out AudioDeviceInfo>) {
            addedDevices.filter { it.isSink && known.add(it.id) && chosen(it) }.forEach { device ->
                // Counted as started even outside the schedule: going pauses it, which does nothing when it wasn't playing.
                started += device.id
                if (schedulePermits()) {
                    // Android moves sound to new headphones a moment after they appear.
                    handler.postDelayed({ player.play() }, SETTLE_MILLIS)
                }
            }
        }

        override fun onAudioDevicesRemoved(removedDevices: Array<out AudioDeviceInfo>) {
            removedDevices.forEach { device ->
                known -= device.id
                if (started.remove(device.id)) {
                    player.pause()
                }
            }
        }
    }

    fun start() {
        audio.getDevices(AudioManager.GET_DEVICES_OUTPUTS).forEach { known += it.id }
        audio.registerAudioDeviceCallback(callback, handler)
    }

    private fun chosen(device: AudioDeviceInfo): Boolean {
        val settings = store.settings.value
        return when {
            device.type in WIRED -> settings.wired
            isBluetooth(device) -> settings.bluetooth && settings.devices.any { matches(it, device) }
            else -> false
        }
    }

    private fun isBluetooth(device: AudioDeviceInfo): Boolean =
        device.type == AudioDeviceInfo.TYPE_BLUETOOTH_A2DP ||
            (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S && device.type == AudioDeviceInfo.TYPE_BLE_HEADSET)

    // By address where Android gives it (9 and later), else by name.
    private fun matches(chosen: ChosenHeadphones, device: AudioDeviceInfo): Boolean =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P && device.address.isNotBlank()) {
            device.address.equals(chosen.address, ignoreCase = true)
        } else {
            device.productName?.toString() == chosen.name
        }

    companion object {
        private const val SETTLE_MILLIS = 1500L
        private val WIRED = setOf(AudioDeviceInfo.TYPE_WIRED_HEADPHONES, AudioDeviceInfo.TYPE_WIRED_HEADSET, AudioDeviceInfo.TYPE_USB_HEADSET)

        /** The paired Bluetooth audio devices, to choose from (empty without "Nearby devices"). */
        @SuppressLint("MissingPermission")
        fun pairedAudio(context: Context): List<ChosenHeadphones> {
            if (!CarReceiver.canReadBluetooth(context)) {
                return emptyList()
            }

            return try {
                context.getSystemService(BluetoothManager::class.java)?.adapter?.bondedDevices.orEmpty()
                    .filter { it.bluetoothClass?.majorDeviceClass == BluetoothClass.Device.Major.AUDIO_VIDEO }
                    .filter { CarReceiver.isCar(context, it).not() }
                    .map { ChosenHeadphones(it.address, it.name ?: it.address) }
                    .sortedBy { it.name.lowercase() }
            } catch (e: SecurityException) {
                emptyList()
            }
        }
    }
}
