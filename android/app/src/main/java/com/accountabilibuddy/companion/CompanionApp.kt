package com.accountabilibuddy.companion

import android.app.Application
import android.os.Build
import android.provider.Settings
import com.accountabilibuddy.companion.data.CompanionStore
import com.accountabilibuddy.companion.data.ImportException
import com.accountabilibuddy.companion.data.Importer
import com.accountabilibuddy.companion.link.DayPlanSync
import com.accountabilibuddy.companion.link.LinkClient
import com.accountabilibuddy.companion.link.LinkDiscovery
import com.accountabilibuddy.companion.link.LinkStore
import com.accountabilibuddy.companion.notify.Refresher
import com.accountabilibuddy.companion.watch.WatchBridge
import com.accountabilibuddy.companion.watch.WatchLinkStore
import com.accountabilibuddy.shared.FocusDayJson
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.serialization.json.JsonObject
import java.io.File

/**
 * The app's single store, the link to the PC (ADR 14), the link to the watch (ADR 21), and the refresh loop for the
 * notification, widget and bedtime reminder.
 */
class CompanionApp : Application() {
    lateinit var store: CompanionStore
        private set

    lateinit var link: LinkClient
        private set

    /** This phone's name as the PC shows it (e.g. "Pixel 7"). */
    lateinit var deviceName: String
        private set

    /** Sends day plan edits made on the phone to the PC (ADR 19). */
    lateinit var planSync: DayPlanSync
        private set

    /** Sends the linked Pixel Watch its snapshot (ADR 21). */
    lateinit var watchBridge: WatchBridge
        private set

    /** True while a screen of the app is showing (set by the screens' view model). */
    @Volatile
    var appOnScreen: Boolean = false

    // Lives as long as the app; the link reads from the PC here.
    private val appScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)

    override fun onCreate() {
        super.onCreate()
        store = CompanionStore(File(filesDir, "companion"))
        deviceName = readDeviceName()
        link = LinkClient(
            LinkStore(File(filesDir, "companion/link.json")), deviceName, deviceKind(), appScope, LinkDiscovery(this), ::applyPcSettings,
            // Without the PC's plan version (a phone-only update, a cleared import) ask for everything again.
            needsFullSettings = { store.state.value.hasImport && store.state.value.pcPlanVersion == null },
            applyFocusDay = { content -> FocusDayJson.read(content)?.let(store::saveFocusDay) },
        )
        planSync = DayPlanSync(store, link, appScope).also { it.start() }
        watchBridge = WatchBridge(this, store, link, WatchLinkStore(File(filesDir, "companion/watch.json")), appScope) { appOnScreen }
            .also { it.start() }
        Refresher.start(this)
    }

    /** The linked PC's profiles, sleep, day bar and events (L2): kept like an import, then the card and widget redraw. */
    private fun applyPcSettings(content: JsonObject, pcName: String): Boolean {
        val data = try {
            Importer.fromLink(content, pcName)
        } catch (e: ImportException) {
            return false
        }

        store.import(data)
        appScope.launch { Refresher.refresh(this@CompanionApp) }
        return true
    }

    /** E.g. "Fabian's Pixel 7" (the name set in Android's settings), else the model. */
    private fun readDeviceName(): String =
        Settings.Global.getString(contentResolver, Settings.Global.DEVICE_NAME)?.takeIf { it.isNotBlank() } ?: Build.MODEL

    private fun deviceKind(): String = if (resources.configuration.smallestScreenWidthDp >= 600) "tablet" else "phone"
}
