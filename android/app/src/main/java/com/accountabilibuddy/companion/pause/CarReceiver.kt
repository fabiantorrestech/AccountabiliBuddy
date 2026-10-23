package com.accountabilibuddy.companion.pause

import android.Manifest
import android.app.UiModeManager
import android.bluetooth.BluetoothClass
import android.bluetooth.BluetoothDevice
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.content.res.Configuration
import android.os.Build
import androidx.core.content.ContextCompat
import com.accountabilibuddy.companion.CompanionApp

/**
 * Notices the phone joining or leaving a car: a Bluetooth device that says it's car audio or a hands-free kit (its
 * class, not its name), or Android's car mode (Android Auto). Android lets this receiver hear Bluetooth connections
 * even while the app isn't running. Needs "Nearby devices" (Bluetooth connect) on Android 12 and later.
 */
class CarReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val app = context.applicationContext as? CompanionApp ?: return
        when (intent.action) {
            BluetoothDevice.ACTION_ACL_CONNECTED, BluetoothDevice.ACTION_ACL_DISCONNECTED -> {
                val device = deviceOf(intent) ?: return
                if (!isCar(context, device)) {
                    return
                }

                app.pause.setInCar(intent.action == BluetoothDevice.ACTION_ACL_CONNECTED || inCarMode(context))
            }
            UiModeManager.ACTION_ENTER_CAR_MODE -> app.pause.setInCar(true)
            UiModeManager.ACTION_EXIT_CAR_MODE -> app.pause.setInCar(false)
        }
    }

    private fun deviceOf(intent: Intent): BluetoothDevice? =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            intent.getParcelableExtra(BluetoothDevice.EXTRA_DEVICE, BluetoothDevice::class.java)
        } else {
            @Suppress("DEPRECATION")
            intent.getParcelableExtra(BluetoothDevice.EXTRA_DEVICE)
        }

    companion object {
        /** True for car audio and hands-free kits, by the device's Bluetooth class. */
        fun isCar(context: Context, device: BluetoothDevice): Boolean {
            if (!canReadBluetooth(context)) {
                return false
            }

            val deviceClass = try {
                device.bluetoothClass?.deviceClass
            } catch (e: SecurityException) {
                null
            }
            return deviceClass == BluetoothClass.Device.AUDIO_VIDEO_CAR_AUDIO || deviceClass == BluetoothClass.Device.AUDIO_VIDEO_HANDSFREE
        }

        fun canReadBluetooth(context: Context): Boolean =
            Build.VERSION.SDK_INT < Build.VERSION_CODES.S ||
                ContextCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_CONNECT) == PackageManager.PERMISSION_GRANTED

        fun inCarMode(context: Context): Boolean =
            (context.resources.configuration.uiMode and Configuration.UI_MODE_TYPE_MASK) == Configuration.UI_MODE_TYPE_CAR
    }
}
