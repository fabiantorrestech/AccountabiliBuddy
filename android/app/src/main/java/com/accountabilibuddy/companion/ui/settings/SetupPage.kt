package com.accountabilibuddy.companion.ui.settings

import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CloudSync
import androidx.compose.material.icons.outlined.PlayCircleOutline
import androidx.compose.runtime.Composable
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.HealthStatus
import com.accountabilibuddy.companion.ui.PermissionsList
import com.accountabilibuddy.companion.ui.Section
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbListRow
import com.accountabilibuddy.companion.ui.permissionsSummary

/**
 * Settings › Setup: "Open the setup again" first (a row of its own), the permissions, and the way back to the Google Drive steps.
 * The row opens today's assistant; the full assistant (every step, with what is done and what was skipped) comes with the setup
 * redesign.
 */
@Composable
fun SetupPage(
    anchor: String?,
    health: HealthStatus,
    onConnectHealth: () -> Unit,
    onAllowUsage: () -> Unit,
    onOpenSetup: () -> Unit,
    onOpenDevices: () -> Unit,
) {
    val context = LocalContext.current
    SettingsPageList(
        listOf(
            PageRow(SettingsAnchors.SETUP) {
                AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
                    AbListRow(
                        title = "Open the setup again",
                        icon = Icons.Outlined.PlayCircleOutline,
                        subtitle = "Linking, permissions and the basics, from the start. Nothing is lost.",
                        onClick = onOpenSetup,
                    )
                }
            },
            PageRow(SettingsAnchors.PERMISSIONS) {
                Section("Permissions", summary = permissionsSummary(context, health), startOpen = true) {
                    PermissionsList(health, onConnectHealth, onAllowUsage)
                }
            },
            PageRow(SettingsAnchors.DRIVE_SETUP) {
                AbCard(contentPadding = PaddingValues(horizontal = 14.dp, vertical = 4.dp), verticalSpacing = 0.dp) {
                    AbListRow(
                        title = "Google Drive setup",
                        icon = Icons.Outlined.CloudSync,
                        subtitle = "Settings › Devices & sync › Google Drive",
                        onClick = onOpenDevices,
                    )
                }
            },
        ),
        anchor,
    )
}
