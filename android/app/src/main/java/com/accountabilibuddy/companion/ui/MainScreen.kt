package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.lifecycle.compose.LifecycleStartEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.R
import com.accountabilibuddy.companion.link.LinkStatus

/** Three tabs: Today, Profiles, Settings; the import password dialog; messages as snackbars. */
@Composable
fun MainScreen(
    viewModel: CompanionViewModel,
    onPickFile: () -> Unit,
    onConnectHealth: () -> Unit,
    onTimelineNotification: (Boolean) -> Unit,
    onAddWidget: () -> Unit,
    onAllowUsage: () -> Unit,
    onConnectCurbox: () -> Unit,
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val model by viewModel.model.collectAsStateWithLifecycle()
    val health by viewModel.health.collectAsStateWithLifecycle()
    val message by viewModel.message.collectAsStateWithLifecycle()
    val passwordRequest by viewModel.passwordRequest.collectAsStateWithLifecycle()
    val importing by viewModel.importing.collectAsStateWithLifecycle()
    val phoneUsage by viewModel.phoneUsage.collectAsStateWithLifecycle()
    val curbox by viewModel.curbox.collectAsStateWithLifecycle()
    val linkStatus by viewModel.linkStatus.collectAsStateWithLifecycle()
    val pairedPc by viewModel.pairedPc.collectAsStateWithLifecycle()
    val pairingOffer by viewModel.pairingOffer.collectAsStateWithLifecycle()
    val pairing by viewModel.pairing.collectAsStateWithLifecycle()
    val pcBusy by viewModel.pcBusy.collectAsStateWithLifecycle()
    val endEarlyTerms by viewModel.endEarlyTerms.collectAsStateWithLifecycle()
    var tab by rememberSaveable { mutableIntStateOf(0) }
    val snackbar = remember { SnackbarHostState() }

    LaunchedEffect(message) {
        message?.let {
            snackbar.showSnackbar(it)
            viewModel.message.value = null
        }
    }

    Scaffold(
        snackbarHost = { SnackbarHost(snackbar) },
        bottomBar = {
            NavigationBar {
                listOf("Today" to R.drawable.ic_tab_today, "Profiles" to R.drawable.ic_tab_profiles, "Settings" to R.drawable.ic_tab_settings)
                    .forEachIndexed { index, (label, icon) ->
                        NavigationBarItem(
                            selected = tab == index,
                            onClick = { tab = index },
                            icon = { Icon(painterResource(icon), contentDescription = null) },
                            label = { Text(label) },
                        )
                    }
            }
        },
    ) { padding ->
        Column(Modifier.padding(padding)) {
            when (tab) {
                0 -> TodayScreen(
                    state, model, viewModel.clock, health, onPickFile, onConnectHealth, viewModel::refreshSleep, phoneUsage, onAllowUsage,
                    showsPcCard = pairedPc != null,
                    pcCard = {
                        PcCard(
                            status = linkStatus,
                            clock = viewModel.clock,
                            busy = pcBusy,
                            onStart = viewModel::startOnPc,
                            onEndEarly = viewModel::beginEndEarly,
                            onRetry = viewModel::retryPc,
                        )
                    },
                )
                1 -> ProfilesScreen(
                    state,
                    viewModel.clock,
                    pcName = pairedPc?.pcName,
                    pcState = (linkStatus as? LinkStatus.Connected)?.state,
                    deviceName = viewModel.deviceName,
                )
                else -> {
                    // Curbox's status shows only here: ask it when Settings opens, or the app comes back to it.
                    LifecycleStartEffect(Unit) {
                        viewModel.refreshCurbox()
                        onStopOrDispose { }
                    }
                    SettingsScreen(
                        state, viewModel.clock, health, importing,
                        onImport = onPickFile,
                        onClearImport = viewModel::clearImport,
                        onSaveSleep = viewModel::saveSleep,
                        onTimelineNotification = onTimelineNotification,
                        onBedtimeReminder = viewModel::setBedtimeReminder,
                        onConnectHealth = onConnectHealth,
                        onAddWidget = onAddWidget,
                        curbox = curbox,
                        onConnectCurbox = onConnectCurbox,
                        pcStatus = linkStatus,
                        pairedPc = pairedPc,
                        onPastePairingLink = viewModel::pastePairingLink,
                        onUnpairPc = viewModel::unpairPc,
                    )
                }
            }
        }
    }

    pairingOffer?.let { offer ->
        PairingDialog(offer, busy = pairing, onPair = viewModel::confirmPairing, onCancel = viewModel::cancelPairing)
    }

    endEarlyTerms?.let { terms ->
        EndEarlyDialog(terms, onEnd = viewModel::finishEndEarly, onCancel = viewModel::cancelEndEarly)
    }

    passwordRequest?.let { request ->
        var password by remember(request) { mutableStateOf("") }
        AlertDialog(
            onDismissRequest = viewModel::cancelPassword,
            title = { Text("Backup password") },
            text = {
                Column {
                    if (request.wrongPassword) {
                        Text("Wrong password. Try again.")
                    }
                    OutlinedTextField(
                        value = password,
                        onValueChange = { password = it },
                        singleLine = true,
                        visualTransformation = PasswordVisualTransformation(),
                        label = { Text("Password") },
                    )
                }
            },
            confirmButton = {
                TextButton(onClick = { viewModel.import(request.uri, password) }, enabled = password.isNotEmpty() && !importing) {
                    Text(if (importing) "Opening…" else "Open")
                }
            },
            dismissButton = { TextButton(onClick = viewModel::cancelPassword) { Text("Cancel") } },
        )
    }
}
