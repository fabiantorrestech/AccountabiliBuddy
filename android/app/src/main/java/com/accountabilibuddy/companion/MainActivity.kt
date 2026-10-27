package com.accountabilibuddy.companion

import android.Manifest
import android.annotation.SuppressLint
import android.appwidget.AppWidgetManager
import android.content.ActivityNotFoundException
import android.content.ComponentName
import android.content.Intent
import android.net.Uri
import android.provider.Settings
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import androidx.health.connect.client.PermissionController
import com.accountabilibuddy.companion.notify.TimelineNotifier
import com.accountabilibuddy.companion.sync.JoinLink
import com.accountabilibuddy.companion.sync.SyncShare
import com.accountabilibuddy.companion.ui.CompanionTheme
import com.accountabilibuddy.companion.ui.CompanionViewModel
import com.accountabilibuddy.companion.ui.MainScreen
import com.accountabilibuddy.companion.widget.TimelineWidgetReceiver

// Play services (the watch link, ADR 21) brings an old androidx.fragment, which lint takes as this activity's; but this
// is a ComponentActivity, not a FragmentActivity, so the old Fragment bug with activity results can't happen here.
@SuppressLint("InvalidFragmentVersionForActivityResult")
class MainActivity : ComponentActivity() {
    private val viewModel: CompanionViewModel by viewModels()

    private val pickFile = registerForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        uri?.let { viewModel.import(it) }
    }

    private val healthPermission = registerForActivityResult(PermissionController.createRequestPermissionResultContract()) {
        viewModel.onHealthPermissionResult()
    }

    private val curboxPermission = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) {
        viewModel.refreshCurbox()
    }

    private val notificationPermission = registerForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        if (!granted) {
            viewModel.message.value = "Without notification permission the timeline can't show in the notification shade."
        }
        viewModel.refreshOutside()
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            CompanionTheme {
                MainScreen(
                    viewModel = viewModel,
                    onPickFile = { pickFile.launch(arrayOf("*/*")) },
                    onConnectHealth = { healthPermission.launch(viewModel.healthPermissions) },
                    onTimelineNotification = { on ->
                        viewModel.setTimelineNotification(on)
                        if (on) {
                            askForNotifications()
                        }
                    },
                    onAddWidget = ::pinWidget,
                    onAllowUsage = ::openUsageAccess,
                    onConnectCurbox = ::connectCurbox,
                )
            }
        }

        if (viewModel.state.value.timelineNotificationOn) {
            askForNotifications()
        }

        if (savedInstanceState == null) {
            handleLink(intent)
        }
    }

    /** A QR code or a shared file opened the app again while it was open. */
    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleLink(intent)
    }

    /**
     * What opened the app: the PC's link code for device sync ("join"), one of its sync QR codes ("sync"), or sync
     * files (*.absync) opened or shared with the app (ADR 22).
     */
    private fun handleLink(intent: Intent?) {
        intent ?: return
        val data = intent.data
        when {
            data?.scheme == SCHEME && data.host == JoinLink.HOST ->
                JoinLink.parse(data.toString())?.let(viewModel::offerJoin) ?: viewModel.showMessage("That link code isn't complete.")
            data?.scheme == SCHEME && data.host == SYNC_HOST -> viewModel.scanSyncCode(data.toString())
            else -> SyncShare.urisIn(intent).takeIf { it.isNotEmpty() }?.let(viewModel::openSyncFiles)
        }
    }

    override fun onStart() {
        super.onStart()
        viewModel.onStart()
    }

    override fun onResume() {
        super.onResume()
        // In front, the app may start the service that pins the card.
        viewModel.refreshOutside()
    }

    override fun onStop() {
        viewModel.onStop()
        super.onStop()
    }

    /** Usage access is a special permission: the user turns it on in Settings. */
    private fun openUsageAccess() {
        val intent = Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS).setData(Uri.fromParts("package", packageName, null))
        try {
            startActivity(intent)
        } catch (e: ActivityNotFoundException) {
            startActivity(Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS))
        }
    }

    private fun connectCurbox() {
        val curboxPackage = viewModel.curboxClient.findPackage() ?: return
        curboxPermission.launch(viewModel.curboxClient.permissionIntent(curboxPackage))
    }

    /** Asks the launcher to place the timeline widget (it shows its own confirmation). */
    private fun pinWidget() {
        val widgets = AppWidgetManager.getInstance(this)
        if (!widgets.isRequestPinAppWidgetSupported) {
            viewModel.message.value = "This home screen can't add widgets from an app. Long-press the home screen, then Widgets."
            return
        }

        widgets.requestPinAppWidget(ComponentName(this, TimelineWidgetReceiver::class.java), null, null)
    }

    private fun askForNotifications() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU && !TimelineNotifier.canPost(this)) {
            notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
        }
    }

    private companion object {
        /** The host of the PC's sync QR codes ([com.accountabilibuddy.shared.sync.SyncQr.LINK_START]). */
        const val SCHEME = "accountabilibuddy"
        const val SYNC_HOST = "sync"
    }
}
