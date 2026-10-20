package com.accountabilibuddy.companion

import android.app.Application
import android.os.Build
import android.provider.Settings
import com.accountabilibuddy.companion.data.CompanionStore
import com.accountabilibuddy.companion.link.LinkClient
import com.accountabilibuddy.companion.link.LinkStore
import com.accountabilibuddy.companion.notify.Refresher
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import java.io.File

/** The app's single store, the link to the PC (ADR 14), and the refresh loop for the notification, widget and bedtime reminder. */
class CompanionApp : Application() {
    lateinit var store: CompanionStore
        private set

    lateinit var link: LinkClient
        private set

    // Lives as long as the app; the link reads from the PC here.
    private val appScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    override fun onCreate() {
        super.onCreate()
        store = CompanionStore(File(filesDir, "companion"))
        link = LinkClient(LinkStore(File(filesDir, "companion/link.json")), deviceName(), deviceKind(), appScope)
        Refresher.start(this)
    }

    /** E.g. "Fabian's Pixel 7" (the name set in Android's settings), else the model. */
    private fun deviceName(): String =
        Settings.Global.getString(contentResolver, Settings.Global.DEVICE_NAME)?.takeIf { it.isNotBlank() } ?: Build.MODEL

    private fun deviceKind(): String = if (resources.configuration.smallestScreenWidthDp >= 600) "tablet" else "phone"
}
