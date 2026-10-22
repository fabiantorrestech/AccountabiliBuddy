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

/** The app header, three tabs (Today, Profiles, Settings), the import password dialog, and messages as snackbars. */
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
    val folderStatus by viewModel.folderStatus.collectAsStateWithLifecycle()
    val runningTasks by viewModel.runningTasks.collectAsStateWithLifecycle()
    val pairedPc by viewModel.pairedPc.collectAsStateWithLifecycle()
    val watchSettings by viewModel.watchSettings.collectAsStateWithLifecycle()
    val detectedWatches by viewModel.detectedWatches.collectAsStateWithLifecycle()
    val pairingOffer by viewModel.pairingOffer.collectAsStateWithLifecycle()
    val joinOffer by viewModel.joinOffer.collectAsStateWithLifecycle()
    val syncReceived by viewModel.syncReceived.collectAsStateWithLifecycle()
    val syncKey by viewModel.syncKey.collectAsStateWithLifecycle()
    val syncSettings by viewModel.syncSettings.collectAsStateWithLifecycle()
    val context = androidx.compose.ui.platform.LocalContext.current
    val pairing by viewModel.pairing.collectAsStateWithLifecycle()
    val pcBusy by viewModel.pcBusy.collectAsStateWithLifecycle()
    val endEarlyTerms by viewModel.endEarlyTerms.collectAsStateWithLifecycle()
    val planConflict by viewModel.planConflict.collectAsStateWithLifecycle()
    var tab by rememberSaveable { mutableIntStateOf(0) }
    val planDraft by viewModel.planDraft.collectAsStateWithLifecycle()
    val snackbar = remember { SnackbarHostState() }

    LaunchedEffect(message) {
        message?.let {
            snackbar.showSnackbar(it)
            viewModel.message.value = null
        }
    }

    Scaffold(
        topBar = { AppHeader() },
        snackbarHost = { SnackbarHost(snackbar) },
        bottomBar = {
            // Hidden while the day plan editor is open: its own Save and Cancel lead out.
            if (planDraft == null) NavigationBar {
                listOf(
                    "Today" to R.drawable.ic_tab_today, "Profiles" to R.drawable.ic_tab_profiles,
                    "Devices" to R.drawable.ic_tab_devices, "Settings" to R.drawable.ic_tab_settings, "About" to R.drawable.ic_tab_about,
                )
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
            planDraft?.let { draft ->
                DayPlanEditorScreen(
                    draft = draft,
                    pcName = pairedPc?.pcName,
                    clock = viewModel.clock,
                    onChange = viewModel::updatePlanDraft,
                    onSave = viewModel::savePlanDraft,
                    onClose = viewModel::closePlanEditor,
                    onMessage = { viewModel.message.value = it },
                )
                return@Column
            }

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
                    onEditPlan = if (pairedPc == null) null else {
                        { viewModel.openPlanEditor()?.let { viewModel.message.value = it } }
                    },
                    onDiscardPlanEdit = viewModel::discardDayPlanEdit,
                    runningTasks = runningTasks,
                )
                1 -> ProfilesScreen(
                    state,
                    viewModel.clock,
                    pcName = pairedPc?.pcName,
                    pcState = (linkStatus as? LinkStatus.Connected)?.state,
                    deviceName = viewModel.deviceName,
                )
                2 -> DevicesScreen(
                    devices = DevicesState(
                        deviceName = viewModel.deviceName,
                        joined = syncKey != null,
                        settings = syncSettings,
                        received = syncReceived,
                        focusDay = state.focusDay,
                        pcName = pairedPc?.pcName,
                        pcConnected = (linkStatus as? LinkStatus.Connected)?.state != null,
                        folder = folderStatus,
                        alarmApps = viewModel.alarmApps(),
                        learnedAlarmApp = viewModel.learnedAlarmApp(),
                    ),
                    actions = DevicesActions(
                        onShare = { viewModel.shareSyncFiles(context) },
                        ownQrLinks = viewModel::ownQrLinks,
                        linkCode = viewModel::syncLinkCode,
                        onJoin = viewModel::joinWithCode,
                        onLeave = viewModel::leaveSyncGroup,
                        onSettings = viewModel::saveSyncSettings,
                        onFolderChosen = viewModel::chooseSyncFolder,
                        onSyncFolderNow = viewModel::syncFolderNow,
                    ),
                    pcStatus = linkStatus,
                    pairedPc = pairedPc,
                    onPastePairingLink = viewModel::pastePairingLink,
                    onUnpairPc = viewModel::unpairPc,
                    watch = WatchSectionState(watchSettings, detectedWatches),
                    watchActions = WatchSectionActions(
                        onFind = viewModel::findWatches,
                        onLink = viewModel::linkWatch,
                        onUnlink = viewModel::unlinkWatch,
                        onDataLayer = viewModel::setWatchDataLayer,
                        onRelay = viewModel::setWatchRelay,
                        onWifi = viewModel::setWatchWifi,
                    ),
                )
                3 -> {
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
                    )
                }
                else -> AboutScreen(pcName = pairedPc?.pcName)
            }
        }
    }

    pairingOffer?.let { offer ->
        // A linked watch with the Wi-Fi fallback on can take the code instead (ADR 21).
        val canPairWatch = watchSettings.linkedNodeId != null && watchSettings.dataLayerOn && watchSettings.wifiFallbackOn
        PairingDialog(
            offer, busy = pairing, onPair = viewModel::confirmPairing, onCancel = viewModel::cancelPairing,
            onPairWatch = if (canPairWatch) viewModel::pairWatchWithPc else null,
        )
    }

    joinOffer?.let { offer ->
        AlertDialog(
            onDismissRequest = viewModel::cancelJoin,
            title = { Text("Join ${offer.pcName}'s devices?") },
            text = {
                Text(
                    "This phone joins your device group: it shares its sync file with your other devices and carries " +
                        "theirs between them, encrypted with this link code.",
                )
            },
            confirmButton = { TextButton(onClick = viewModel::confirmJoin) { Text("Join") } },
            dismissButton = { TextButton(onClick = viewModel::cancelJoin) { Text("Cancel") } },
        )
    }

    planConflict?.let { conflict ->
        DayPlanConflictDialog(conflict, pairedPc?.pcName, viewModel.clock, onKeepPc = viewModel::keepPcPlan, onUseMine = viewModel::useMyPlan)
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
