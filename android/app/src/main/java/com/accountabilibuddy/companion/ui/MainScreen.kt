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
import androidx.compose.animation.togetherWith
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
import com.accountabilibuddy.companion.sync.PcFileControl
import com.accountabilibuddy.shared.sync.SyncAction

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
    val folderStatus by viewModel.folderStatus.collectAsStateWithLifecycle()
    val driveStatus by viewModel.driveStatus.collectAsStateWithLifecycle()
    val relayStatus by viewModel.relayStatus.collectAsStateWithLifecycle()
    val pauseState by viewModel.pauseState.collectAsStateWithLifecycle()
    val lunchTaken by (androidx.compose.ui.platform.LocalContext.current.applicationContext as com.accountabilibuddy.companion.CompanionApp).lunch.lunch.collectAsStateWithLifecycle()
    val lunchLength by (androidx.compose.ui.platform.LocalContext.current.applicationContext as com.accountabilibuddy.companion.CompanionApp).lunch.lengthMinutes.collectAsStateWithLifecycle()
    val runningTasks by viewModel.runningTasks.collectAsStateWithLifecycle()
    val comebacks by viewModel.comebacks.collectAsStateWithLifecycle()
    val beeminder by viewModel.beeminder.collectAsStateWithLifecycle()
    val watchSettings by viewModel.watchSettings.collectAsStateWithLifecycle()
    val detectedWatches by viewModel.detectedWatches.collectAsStateWithLifecycle()
    val joinOffer by viewModel.joinOffer.collectAsStateWithLifecycle()
    val syncReceived by viewModel.syncReceived.collectAsStateWithLifecycle()
    val syncKey by viewModel.syncKey.collectAsStateWithLifecycle()
    val syncSettings by viewModel.syncSettings.collectAsStateWithLifecycle()
    val context = androidx.compose.ui.platform.LocalContext.current
    var nudgeRepeat by remember { mutableIntStateOf(com.accountabilibuddy.companion.notify.WorkStartNudge.repeatMinutes(context)) }
    var showSetup by remember { mutableStateOf(!SetupState.shown(context)) }
    var takeoverOn by remember { mutableStateOf(com.accountabilibuddy.companion.nudge.NudgeStore(context).load().takeoverOn) }
    val endEarlyTerms by viewModel.endEarlyTerms.collectAsStateWithLifecycle()
    val planConflict by viewModel.planConflict.collectAsStateWithLifecycle()
    val controlPc by viewModel.controlPc.collectAsStateWithLifecycle()
    val pcWaiting by viewModel.pcWaiting.collectAsStateWithLifecycle()
    val pcChoices by viewModel.pcChoices.collectAsStateWithLifecycle()
    val chosenPcId by viewModel.chosenPcId.collectAsStateWithLifecycle()
    // ADR 42, 45: the PC card and plan edits go through the controlled PC's sync file.
    val pcName = controlPc?.file?.device?.name
    val canControlPc = controlPc != null
    var tab by rememberSaveable { mutableIntStateOf(0) }
    val planDraft by viewModel.planDraft.collectAsStateWithLifecycle()
    // The migration assistant (5 taps on About's logo, or the Today card).
    var whatsNewOpen by androidx.compose.runtime.saveable.rememberSaveable { mutableStateOf(false) }
    var whatsNewTick by remember { androidx.compose.runtime.mutableIntStateOf(0) }
    val appVersion = remember { (context.applicationContext as com.accountabilibuddy.companion.CompanionApp).appVersion() }
    val planDayOpen by viewModel.planDayOpen.collectAsStateWithLifecycle()
    val snackbar = remember { SnackbarHostState() }
    val noise = (context.applicationContext as com.accountabilibuddy.companion.CompanionApp).noise
    val noiseState by noise.state.collectAsStateWithLifecycle()
    val noiseSettings by noise.settings.collectAsStateWithLifecycle()

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
            if (planDraft == null) Column {
            NoiseBar(noiseState, noiseSettings, noise) { viewModel.message.value = it }
            NavigationBar {
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
            }
        },
    ) { padding ->
        Column(Modifier.padding(padding)) {
            if (showSetup) {
                SetupScreen(health, onConnectHealth, onAllowUsage, onConnectCurbox, viewModel::setReenableTime) {
                    SetupState.markShown(context)
                    showSetup = false
                }
                return@Column
            }

            if (planDayOpen) {
                PlanDayScreen(
                    plan = state.document.dayPlan,
                    blocks = remember { viewModel.planDayBlocks() },
                    onSave = viewModel::savePlannedDays,
                    onClose = { viewModel.planDayOpen.value = false },
                    slept = state.slept,
                    loadBlocks = viewModel::planDayBlocks,
                )
                return@Column
            }

            if (whatsNewOpen) {
                WhatsNewScreen(onConnectCurbox, viewModel::setReenableTime) { whatsNewOpen = false }
                return@Column
            }

            planDraft?.let { draft ->
                DayPlanEditorScreen(
                    draft = draft,
                    startTab = viewModel.planEditorTab,
                    pcName = pcName,
                    clock = viewModel.clock,
                    onChange = viewModel::updatePlanDraft,
                    onSave = viewModel::savePlanDraft,
                    onClose = viewModel::closePlanEditor,
                    onMessage = { viewModel.message.value = it },
                )
                return@Column
            }

            val scale = LocalMotionScale.current
            androidx.compose.animation.AnimatedContent(
                targetState = tab,
                transitionSpec = {
                    val toRight = targetState > initialState
                    (androidx.compose.animation.fadeIn(motionSpec(scale, 220)) +
                        androidx.compose.animation.slideInHorizontally(motionSpec(scale, 220)) { width -> if (toRight) width / 8 else -width / 8 })
                        .togetherWith(androidx.compose.animation.fadeOut(motionSpec(scale, 150)))
                },
                label = "tabs",
            ) { shown ->
            when (shown) {
                0 -> Column {
                    WhatsNewCard(appVersion, onSetUp = { whatsNewOpen = true }, onDismissed = { whatsNewTick++ })
                    DayOffCard()
                    LunchCard(
                        workday = state.document.dayPlan.isWorkday(java.time.LocalDate.now()),
                        flex = state.document.dayPlan.flexibleBreakOn(java.time.LocalDate.now()),
                        taken = lunchTaken?.takeIf { it.start.atZone(java.time.ZoneId.systemDefault()).toLocalDate() == java.time.LocalDate.now() },
                        lengthMinutes = lunchLength,
                        clock = viewModel.clock,
                        onStart = { (context.applicationContext as com.accountabilibuddy.companion.CompanionApp).lunch.start() },
                        onBack = { (context.applicationContext as com.accountabilibuddy.companion.CompanionApp).lunch.endNow() },
                        onAdjust = { longer -> (context.applicationContext as com.accountabilibuddy.companion.CompanionApp).lunch.adjust(longer) },
                    )
                    TodayScreen(
                    state, model, viewModel.clock, health, onPickFile, onConnectHealth, viewModel::refreshSleep, phoneUsage, onAllowUsage,
                    showsPcCard = true,
                    pcCard = {
                        val pc = controlPc
                        if (pc == null) {
                            PcMissingCard()
                        } else {
                            PcFileCard(
                                pcName = pc.file.device.name,
                                state = PcFileControl.stateOf(pc.file, java.time.Instant.now()),
                                writtenAt = pc.file.device.writtenAt,
                                updateNeeded = PcFileControl.updateNeeded(pc.file.device),
                                waiting = pcWaiting?.takeIf { it.target == pc.file.device.deviceId },
                                clock = viewModel.clock,
                                onStart = viewModel::startOnPc,
                                onEndEarly = viewModel::beginEndEarly,
                                onCancelWaiting = viewModel::cancelPcWaiting,
                                // Worked out again when the bar's model ticks or the plan changes.
                                pace = remember(model, state.document.dayPlan) {
                                    BudgetPaceLine.today(context.applicationContext as com.accountabilibuddy.companion.CompanionApp, java.time.LocalDateTime.now())
                                },
                            )
                        }
                    },
                    onEditPlan = if (!canControlPc) null else {
                        { viewModel.openPlanEditor()?.let { viewModel.message.value = it } }
                    },
                    onDiscardPlanEdit = viewModel::discardDayPlanEdit,
                    onPlanDay = if (!canControlPc) null else {
                        { viewModel.planDayOpen.value = true }
                    },
                    onSetWeek = if (!canControlPc) null else {
                        { weekB -> viewModel.setThisWeek(weekB)?.let { viewModel.message.value = it } }
                    },
                    runningTasks = runningTasks,
                    comebacks = comebacks,
                    beeminder = beeminder,
                    timersCard = {
                        TimersCard((context.applicationContext as com.accountabilibuddy.companion.CompanionApp).timers, viewModel.clock)
                    },
                )
                }
                1 -> ProfilesScreen(
                    state,
                    viewModel.clock,
                    pcName = pcName,
                    pcState = controlPc?.let { PcFileControl.stateOf(it.file, java.time.Instant.now()) },
                    deviceName = viewModel.deviceName,
                    curbox = curbox,
                )
                2 -> DevicesScreen(
                    devices = DevicesState(
                        deviceName = viewModel.deviceName,
                        joined = syncKey != null,
                        settings = syncSettings,
                        received = syncReceived,
                        folder = folderStatus,
                        drive = driveStatus,
                        relay = relayStatus,
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
                        beginDriveSignIn = viewModel::beginDriveSignIn,
                        onDriveSignInResult = viewModel::finishDriveSignIn,
                        onSyncDriveNow = viewModel::syncDriveNow,
                        onSyncRelayNow = viewModel::syncRelayNow,
                    ),
                    watch = WatchSectionState(watchSettings, detectedWatches),
                    watchActions = WatchSectionActions(
                        onFind = viewModel::findWatches,
                        onLink = viewModel::linkWatch,
                        onUnlink = viewModel::unlinkWatch,
                        onDataLayer = viewModel::setWatchDataLayer,
                        onRelay = viewModel::setWatchRelay,
                    ),
                    pcChoices = pcChoices,
                    chosenPcId = chosenPcId,
                    onChoosePc = viewModel::choosePc,
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
                        onReenableTime = viewModel::setReenableTime,
                        onConnectHealth = onConnectHealth,
                        onAddWidget = onAddWidget,
                        curbox = curbox,
                        onConnectCurbox = onConnectCurbox,
                        noise = noise,
                        noiseSettings = noiseSettings,
                        pause = pauseState,
                        onAllowUsage = onAllowUsage,
                        onOpenSetup = { showSetup = true },
                        wakeAlarm = { WakeAlarmRows(syncSettings, viewModel.alarmApps(), viewModel.learnedAlarmApp(), viewModel::saveSyncSettings) },
                        wakeAlarmSummary = if (syncSettings.alarmOn) "On: your next alarm is the wake time" else "Off: the Sleep part's wake time",
                        devicesSummary = listOfNotNull(
                            if (syncKey != null) "In your device group" else "Not linked yet",
                            "Drive".takeIf { syncSettings.driveOn },
                            "relay".takeIf { syncSettings.relayOn },
                            pcName,
                        ).joinToString(" · "),
                        onOpenDevices = { tab = 2 },
                        pauseActions = PauseActions(
                            onRequestOff = viewModel::requestPause,
                            onTurnOn = viewModel::turnPauseOff,
                            onCarPause = viewModel::setCarPause,
                            onPlanOffDays = { viewModel.openPlanEditor(startTab = 2)?.let { viewModel.message.value = it } },
                            nudgeRepeat = nudgeRepeat,
                            takeoverOn = takeoverOn,
                            takeoverAllowed = com.accountabilibuddy.companion.nudge.Takeover.canShow(context),
                            onTakeover = { on ->
                                com.accountabilibuddy.companion.nudge.NudgeStore(context).update { it.copy(takeoverOn = on) }
                                takeoverOn = on
                            },
                            onAllowTakeover = {
                                context.startActivity(
                                    android.content.Intent(android.provider.Settings.ACTION_MANAGE_OVERLAY_PERMISSION, android.net.Uri.parse("package:${context.packageName}")),
                                )
                            },
                            onNudgeRepeat = { minutes ->
                                com.accountabilibuddy.companion.notify.WorkStartNudge.setRepeatMinutes(context, minutes)
                                nudgeRepeat = minutes
                            },
                        ),
                    )
                }
                else -> AboutScreen(pcName = pcName, onOpenWhatsNew = { whatsNewOpen = true })
            }
            }
        }
    }

    joinOffer?.let { prompt ->
        JoinPromptDialog(prompt, onContinue = viewModel::continueJoin, onCancel = viewModel::cancelJoin)
    }

    planConflict?.let { conflict ->
        DayPlanConflictDialog(conflict, viewModel.conflictPcName(), viewModel.clock, onKeepPc = viewModel::keepPcPlan, onUseMine = viewModel::useMyPlan)
    }

    endEarlyTerms?.let { terms ->
        EndEarlyDialog(
            terms, onEnd = viewModel::finishEndEarly, onCancel = viewModel::cancelEndEarly,
            sending = pcWaiting?.kind == SyncAction.END_EARLY,
        )
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
