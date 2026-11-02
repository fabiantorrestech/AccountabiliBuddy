package com.accountabilibuddy.companion.ui

import androidx.activity.compose.BackHandler
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.OutlinedTextField
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
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.LifecycleStartEffect
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.accountabilibuddy.companion.sync.PcFileControl
import com.accountabilibuddy.companion.sync.RunningTasks
import com.accountabilibuddy.companion.ui.plan.PlanWaitSheet
import com.accountabilibuddy.companion.ui.shell.AppDestination
import com.accountabilibuddy.companion.ui.shell.AppScaffold
import com.accountabilibuddy.companion.ui.shell.rememberAppNav
import com.accountabilibuddy.companion.ui.settings.SettingsDetailHost
import com.accountabilibuddy.companion.ui.settings.SettingsPageActions
import com.accountabilibuddy.companion.ui.settings.SettingsPageInputs
import com.accountabilibuddy.companion.ui.shell.rememberNavLayout
import com.accountabilibuddy.shared.sync.SyncAction

/**
 * The shell: the five places (Today, Tasks, Plan, Stats, Settings) with the bottom bar, the modal screens that take the whole app
 * (Unlinked, Setup, Plan day, What's new, the plan editor), the dialogs, and messages as snackbars.
 */
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
    val syncAllReceived by viewModel.syncAllReceived.collectAsStateWithLifecycle()
    val syncKey by viewModel.syncKey.collectAsStateWithLifecycle()
    val syncSettings by viewModel.syncSettings.collectAsStateWithLifecycle()
    // ADR 54, 55: a phone that was removed from its group, the removed devices kept, and the remove and restore dialogs.
    val unlinked by viewModel.unlinked.collectAsStateWithLifecycle()
    val removedDevices by viewModel.removedDevices.collectAsStateWithLifecycle()
    // The focus-session lock, looked at again every 15 s, so the Unlinked screen's buttons come back when it lifts.
    val removalLocked by viewModel.removalLocked.collectAsStateWithLifecycle()
    // The guard settings are view-only while a focus session runs; null otherwise. Looked at again every few seconds.
    val guardsLock by viewModel.guardsLock.collectAsStateWithLifecycle()
    val removeState by viewModel.removeDevice.state.collectAsStateWithLifecycle()
    val syncOffPending by viewModel.syncOffPending.collectAsStateWithLifecycle()
    val restoreState by viewModel.restoreBackup.state.collectAsStateWithLifecycle()
    val context = androidx.compose.ui.platform.LocalContext.current
    var nudgeRepeat by remember { mutableIntStateOf(com.accountabilibuddy.companion.notify.WorkStartNudge.repeatMinutes(context)) }
    var showSetup by remember { mutableStateOf(!SetupState.shown(context)) }
    var takeoverOn by remember { mutableStateOf(com.accountabilibuddy.companion.nudge.NudgeStore(context).load().takeoverOn) }
    val endEarlyTerms by viewModel.endEarlyTerms.collectAsStateWithLifecycle()
    val planConflict by viewModel.planConflict.collectAsStateWithLifecycle()
    val planWait by viewModel.planWait.collectAsStateWithLifecycle()
    val controlPc by viewModel.controlPc.collectAsStateWithLifecycle()
    val pcWaiting by viewModel.pcWaiting.collectAsStateWithLifecycle()
    val pcChoices by viewModel.pcChoices.collectAsStateWithLifecycle()
    val chosenPcId by viewModel.chosenPcId.collectAsStateWithLifecycle()
    // ADR 42, 45: the PC card and plan edits go through the controlled PC's sync file.
    val pcName = controlPc?.file?.device?.name
    val canControlPc = controlPc != null
    // Where the user is in the five places (the old `tab` number), kept across rotation and the process being recreated.
    val nav = rememberAppNav()
    val navLayout = rememberNavLayout()
    val syncNow = rememberSyncNow()
    val settingsListState = rememberLazyListState()
    // The Settings search text: in memory only (never saved or sent), and gone when the user leaves Settings.
    var settingsQuery by remember { mutableStateOf("") }
    LaunchedEffect(nav.destination) { if (nav.destination != AppDestination.Settings) settingsQuery = "" }
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
    val openRequest by viewModel.openRequest.collectAsStateWithLifecycle()

    LaunchedEffect(message) {
        message?.let {
            snackbar.showSnackbar(it)
            viewModel.message.value = null
        }
    }

    // A route from MainActivity's `ab.open` extra. It only navigates: nothing is started or unlocked by it, and a phone that was
    // removed from its group (the Unlinked screen has no way into a syncing page) takes no route at all.
    LaunchedEffect(openRequest) {
        val path = openRequest ?: return@LaunchedEffect
        viewModel.openRequest.value = null
        if (unlinked == null) nav.open(path)
    }

    // Back: out of a page inside a place, then to Today, then out of the app. The modal screens keep what they did before
    // (Back leaves the app from them).
    val modalOpen = unlinked != null || showSetup || planDayOpen || whatsNewOpen || planDraft != null
    BackHandler(enabled = nav.canGoBack && !modalOpen) { nav.back() }

    val pcState = controlPc?.let { PcFileControl.stateOf(it.file, java.time.Instant.now()) }

    // The phone's guard switches (Settings › Guards), with the work-start nudge and the full-screen work card.
    val pauseActions = PauseActions(
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
    )

    androidx.compose.runtime.CompositionLocalProvider(LocalGuardsLock provides guardsLock) {
    AppScaffold(
        nav = nav,
        layout = navLayout,
        snackbar = snackbar,
        // Hidden while the day plan editor is open (its own Save and Cancel lead out) and while the phone is unlinked (ADR 54):
        // that screen is the whole app until the user chooses. Every other screen, a running focus session included, has the bar.
        showNav = planDraft == null && unlinked == null,
        // "Park a thought" sits above the bar on Today only: Tasks has its own "Add a task" field there (board 5v).
        parkPill = if (nav.destination == AppDestination.Today) {
            { ParkPill() }
        } else {
            null
        },
    ) { padding ->
        Column(Modifier.padding(padding)) {
            // ADR 54: removed from the group, or cut off by a removal. Nothing syncs and no other screen shows until the user chooses.
            unlinked?.let { state ->
                UnlinkedScreen(
                    state = state,
                    lockedReason = removalLocked,
                    onJoin = viewModel::joinWithCode,
                    onContinueUnsynced = {
                        // Lands on Today, with the one-time note shown as a message.
                        if (viewModel.continueUnsynced()) nav.select(AppDestination.Today)
                    },
                    onStartFromScratch = viewModel::startFromScratch,
                )
                return@Column
            }

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
                // Keyed on the whole route, so the pane that fades out keeps the page it showed (a Settings page does not flash the list).
                targetState = nav.route,
                transitionSpec = {
                    val sameplace = targetState.destination == initialState.destination
                    val toRight = targetState.destination.ordinal > initialState.destination.ordinal
                    if (sameplace) {
                        androidx.compose.animation.fadeIn(motionSpec(scale, 160)).togetherWith(androidx.compose.animation.fadeOut(motionSpec(scale, 100)))
                    } else {
                        (androidx.compose.animation.fadeIn(motionSpec(scale, 220)) +
                            androidx.compose.animation.slideInHorizontally(motionSpec(scale, 220)) { width -> if (toRight) width / 8 else -width / 8 })
                            .togetherWith(androidx.compose.animation.fadeOut(motionSpec(scale, 150)))
                    }
                },
                label = "places",
            ) { shown ->
            when (shown.destination) {
                AppDestination.Today -> {
                    val app = context.applicationContext as com.accountabilibuddy.companion.CompanionApp
                    // The PC whose session Today shows: the controlled PC while it runs one, else any other linked PC with a live session
                    // (so a running session is never "Nothing running"); with none running, the controlled PC for Start. Looked at
                    // again with the bar's model (every 30 s) and when the files change.
                    val todayPc = remember(controlPc, syncReceived, model, pcWaiting, runningTasks) {
                        val now = java.time.Instant.now()
                        fun running(d: com.accountabilibuddy.companion.sync.ReceivedDevice) = PcFileControl.liveRunning(d.file, now) != null
                        val device = controlPc?.takeIf { running(it) } ?: PcFileControl.pcs(syncReceived).firstOrNull { running(it) } ?: controlPc
                        TodayPc(
                            name = device?.file?.device?.name,
                            pcId = device?.file?.device?.deviceId,
                            state = device?.let { PcFileControl.stateOf(it.file, now) },
                            awaySince = device?.let { PcFileControl.liveRunning(it.file, now)?.awaySince },
                            updateNeeded = device?.let { PcFileControl.updateNeeded(it.file.device) },
                            waiting = pcWaiting?.takeIf { it.target == device?.file?.device?.deviceId },
                            // The shown PC's own checklist (from its file), not the newest file's, so two running PCs each keep theirs.
                            tasks = device?.let { d -> PcFileControl.stateOf(d.file, now).takeIf { it.isActive && it.tasks.isNotEmpty() } }
                                ?.let { RunningTasks(device.file.device.name, it.tasks, it.currentTask) },
                        )
                    }
                    val todayLunch = TodayLunch(
                        workday = state.document.dayPlan.isWorkday(java.time.LocalDate.now()),
                        flex = state.document.dayPlan.flexibleBreakOn(java.time.LocalDate.now()),
                        fixed = state.document.dayPlan.fixedLunchOn(java.time.LocalDate.now()),
                        taken = lunchTaken?.takeIf { it.start.atZone(java.time.ZoneId.systemDefault()).toLocalDate() == java.time.LocalDate.now() },
                        lengthMinutes = lunchLength,
                        onStart = { app.lunch.start() },
                        onBack = { app.lunch.endNow() },
                        onAdjust = { longer -> app.lunch.adjust(longer) },
                    )
                    TodayPane(
                        state = state, model = model, clock = viewModel.clock,
                        pc = todayPc,
                        comebacks = comebacks,
                        phoneUsage = phoneUsage,
                        guardsLock = guardsLock,
                        guardOn = !com.accountabilibuddy.companion.pause.PauseRules.isQuiet(pauseState),
                        lunch = todayLunch,
                        beeminder = beeminder,
                        // The noise player (it was a bar above the tabs): a card once a sound folder is set; the folder is chosen
                        // in Settings › Alerts & on-screen.
                        noiseCard = if (noiseState.hasFolder) {
                            { NoiseCard(noiseState, noiseSettings, noise) }
                        } else {
                            null
                        },
                        actions = TodayActions(
                            onStartOnPc = viewModel::startOnPc,
                            onEndEarly = { viewModel.beginEndEarly(todayPc.pcId) },
                            onCancelWaiting = { viewModel.cancelPcWaiting(todayPc.pcId) },
                            onImport = onPickFile,
                            onOpenDevices = { nav.open(AppDestination.Settings, SettingsCategory.Devices.key) },
                            onOpenTasks = { nav.select(AppDestination.Tasks) },
                            onAllowUsage = onAllowUsage,
                        ),
                        syncLight = {
                            // The light reads the group's files and the methods' last results; the alarm apps are not needed here.
                            val lightDevices = remember(viewModel.deviceName, syncKey, syncSettings, syncReceived, folderStatus, driveStatus, relayStatus) {
                                DevicesState(
                                    deviceName = viewModel.deviceName, joined = syncKey != null, settings = syncSettings, received = syncReceived,
                                    folder = folderStatus, drive = driveStatus, relay = relayStatus,
                                )
                            }
                            SyncLightHost(
                                devices = lightDevices,
                                watch = WatchSectionState(watchSettings, detectedWatches),
                                detectedWatchIds = detectedWatches.map { it.id }.toSet(),
                                syncNow = syncNow,
                                onOpenDevices = { nav.open(AppDestination.Settings, SettingsCategory.Devices.key) },
                            )
                        },
                        banner = { WhatsNewCard(appVersion, onSetUp = { whatsNewOpen = true }, onDismissed = { whatsNewTick++ }) },
                    )
                }
                AppDestination.Tasks -> TasksScreen(controlPc = controlPc, snackbar = snackbar)
                AppDestination.Plan -> PlanScreen(
                    state, model, viewModel.clock,
                    pcName = pcName,
                    canChange = canControlPc,
                    guardsLock = guardsLock,
                    planWait = planWait,
                    onChangeDay = { viewModel.openPlanDay()?.let { viewModel.message.value = it } },
                    onTemplates = { viewModel.openPlanEditor()?.let { viewModel.message.value = it } },
                    onCategories = { viewModel.openPlanEditor(startTab = 3)?.let { viewModel.message.value = it } },
                    onSetWeek = { week -> viewModel.setThisWeek(week)?.let { viewModel.message.value = it } },
                    onDiscardPlanEdit = viewModel::discardDayPlanEdit,
                )
                AppDestination.Stats -> StatsScreen(state, syncAllReceived, syncReceived, health, onConnectHealth, viewModel::refreshSleep, phoneUsage, onAllowUsage, routeView = shown.detail)
                AppDestination.Settings -> {
                    // Curbox's status shows only in Settings (Connections): ask it when Settings opens, or the app comes back to it.
                    LifecycleStartEffect(Unit) {
                        viewModel.refreshCurbox()
                        onStopOrDispose { }
                    }
                    val category = SettingsCategory.fromKey(shown.detail)
                    val backToList = { nav.open(AppDestination.Settings) }
                    val devicesSummary = listOfNotNull(
                        if (syncKey != null) "In your device group" else "Not linked yet",
                        "Drive".takeIf { syncSettings.driveOn },
                        "relay".takeIf { syncSettings.relayOn },
                        pcName,
                    ).joinToString(" · ")
                    if (category == null) {
                        SettingsList(
                            SettingsListInputs(
                                state = state, health = health, lunchLengthMinutes = lunchLength, pause = pauseState, takeoverOn = takeoverOn,
                                curbox = curbox, devicesSummary = devicesSummary, appVersion = appVersion, guardsLock = guardsLock,
                            ),
                            onOpen = { nav.open(AppDestination.Settings, it.key) },
                            query = settingsQuery,
                            onQueryChange = { settingsQuery = it },
                            onOpenResult = { nav.open(AppDestination.Settings, it.category.key, it.anchor) },
                            listState = settingsListState,
                        )
                    } else {
                        SettingsDetailHost(
                            category = category,
                            anchor = shown.anchor,
                            onBack = backToList,
                            inputs = SettingsPageInputs(
                                state = state, clock = viewModel.clock, health = health, importing = importing, curbox = curbox,
                                noise = noise, noiseSettings = noiseSettings, pause = pauseState, guardsLock = guardsLock,
                                pcName = pcName, pcState = pcState, deviceName = viewModel.deviceName,
                                wakeAlarm = { WakeAlarmRows(syncSettings, viewModel.alarmApps(), viewModel.learnedAlarmApp(), viewModel::saveSyncSettings) },
                                wakeAlarmSummary = if (syncSettings.alarmOn) "On: your next alarm is the wake time" else "Off: the Sleep part's wake time",
                            ),
                            actions = SettingsPageActions(
                                onImport = onPickFile,
                                onClearImport = viewModel::clearImport,
                                onSaveSleep = viewModel::saveSleep,
                                onTimelineNotification = onTimelineNotification,
                                onConnectHealth = onConnectHealth,
                                onAddWidget = onAddWidget,
                                onConnectCurbox = onConnectCurbox,
                                onAllowUsage = onAllowUsage,
                                onOpenSetup = { showSetup = true },
                                onOpenDevices = { nav.open(AppDestination.Settings, SettingsCategory.Devices.key) },
                                onOpenPlan = { nav.select(AppDestination.Plan) },
                                onOpenWhatsNew = { whatsNewOpen = true },
                                onReenableTime = viewModel::setReenableTime,
                                pause = pauseActions,
                            ),
                            devices = DevicesPageInputs(
                                devices = DevicesState(
                                    deviceName = viewModel.deviceName,
                                    joined = syncKey != null,
                                    settings = syncSettings,
                                    received = syncReceived,
                                    allReceived = syncAllReceived,
                                    folder = folderStatus,
                                    drive = driveStatus,
                                    relay = relayStatus,
                                    alarmApps = viewModel.alarmApps(),
                                    learnedAlarmApp = viewModel.learnedAlarmApp(),
                                    removed = removedDevices,
                                    removalLocked = removalLocked,
                                    appVersion = appVersion,
                                    syncOffPending = syncOffPending,
                                ),
                                actions = DevicesActions(
                                    onRemoveDevice = viewModel::openRemoveDevice,
                                    onRestore = viewModel.restoreBackup::open,
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
                                    onCancelSyncOff = viewModel::cancelSyncOff,
                                    turnOffWaits = viewModel::turnOffWaits,
                                    repointWaits = viewModel::repointWaits,
                                    countsAsWay = viewModel::countsAsWay,
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
                                syncNow = syncNow,
                            ),
                        )
                    }
                }
            }
            }
        }
    }
    }

    joinOffer?.let { prompt ->
        JoinPromptDialog(prompt, onContinue = viewModel::continueJoin, onCancel = viewModel::cancelJoin)
    }

    removeState?.let { RemoveDeviceSheet(it, viewModel.removeDevice) }
    restoreState?.let { RestoreBackupSheet(it, viewModel.restoreBackup) }

    // A plan edit the PC took and is holding (ADR 28): says when it applies on its own. The wait is the PC's; there is no "apply now".
    planWait?.takeIf { !it.seen && it.appliesAt.isAfter(java.time.Instant.now()) }?.let { wait ->
        PlanWaitSheet(wait, onDismiss = viewModel::planWaitSeen)
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
