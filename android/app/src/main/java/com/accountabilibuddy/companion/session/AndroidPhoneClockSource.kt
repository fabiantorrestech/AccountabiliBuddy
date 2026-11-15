package com.accountabilibuddy.companion.session

import android.content.Context
import android.os.Build
import android.os.SystemClock
import android.provider.Settings
import com.accountabilibuddy.companion.guidance.TrustedTime
import com.accountabilibuddy.shared.session.PhoneClockSource
import com.accountabilibuddy.shared.session.PhoneTimeReading

/** What the phone session's clock reads on a phone: the elapsed clock, the boot count and [TrustedTime] with its network-backed signal. */
class AndroidPhoneClockSource(private val context: Context) : PhoneClockSource {
    override fun elapsedRealtimeMs(): Long = SystemClock.elapsedRealtime()

    override fun bootCount(): Int = runCatching { Settings.Global.getInt(context.contentResolver, Settings.Global.BOOT_COUNT, -1) }.getOrDefault(-1)

    // Network time can come from Android 13 on (TrustedTime reads it only there).
    override fun trustCanCome(): Boolean = Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU

    override fun trustedReading(): PhoneTimeReading = TrustedTime.reading().let { PhoneTimeReading(it.instant, it.networkBacked) }
}
