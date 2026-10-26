package com.accountabilibuddy.companion.noise

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.media.AudioAttributes
import android.media.AudioDeviceCallback
import android.media.AudioDeviceInfo
import android.media.AudioFocusRequest
import android.media.AudioManager
import android.media.MediaMetadata
import android.media.MediaPlayer
import android.media.session.MediaSession
import android.media.session.PlaybackState
import android.net.Uri
import android.os.Handler
import android.os.Looper
import android.provider.DocumentsContract
import androidx.core.content.ContextCompat
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch

/**
 * The noise player, ported from the PC: sounds from a folder the user picks, play/pause, previous/next, its own volume
 * and repeat. With [NoiseSettings.systemControls] on it's media: a media session (Android's controls, headset buttons)
 * and audio focus (it pauses for other audio). Off, it asks for neither, so other audio plays over it. While playing,
 * [NoiseService] keeps it alive in the foreground. With [NoiseSettings.headphoneLock] on it plays only while headphones
 * are connected (every way of starting it comes through [play]); it pauses when the last headphones go, locked or with
 * [NoiseSettings.stopOnDisconnect]. Use from the main thread.
 */
class NoisePlayer(private val context: Context, private val store: NoiseSettingsStore, private val scope: CoroutineScope) {
    private val _state = MutableStateFlow(NoiseState())
    private val audio = context.getSystemService(AudioManager::class.java)
    private var player: MediaPlayer? = null
    private var preparedIndex = -1
    private var files: List<NoiseFile> = emptyList()
    private var index = -1
    private var session: MediaSession? = null
    private var focus: AudioFocusRequest? = null
    private var pausedForFocus = false
    private val handler = Handler(Looper.getMainLooper())
    private var noisyRegistered = false

    /** True when the last [play] was refused by the lock (the service then says why). */
    var lastPlayRefused = false
        private set

    // Android says "becoming noisy" before it moves sound to the speaker (wired unplugged, Bluetooth gone): pause
    // then, so nothing leaks through the speaker. Listened to only while playing or paused for another app's sound.
    private val noisy = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            val settings = store.settings.value
            if (intent.action == AudioManager.ACTION_AUDIO_BECOMING_NOISY && (settings.headphoneLock || settings.stopOnDisconnect)) {
                pause()
            }
        }
    }

    private val clearHint = Runnable { _state.value = _state.value.copy(needsHeadphones = false); onChanged?.invoke() }

    // Headphones coming and going: kept in the state; the last ones going pauses it when locked or "Stop when
    // headphones disconnect" is on.
    private val devices = object : AudioDeviceCallback() {
        override fun onAudioDevicesAdded(addedDevices: Array<out AudioDeviceInfo>) {
            headphones(Headphones.connected(audio))
        }

        override fun onAudioDevicesRemoved(removedDevices: Array<out AudioDeviceInfo>) {
            val wasConnected = _state.value.headphonesConnected
            val connected = Headphones.connected(audio, ignoring = removedDevices.map { it.id }.toSet())
            headphones(connected)
            val settings = store.settings.value
            // Paused for another app's sound counts as playing: it mustn't come back through the speaker.
            if (wasConnected && !connected && (_state.value.playing || pausedForFocus) && (settings.headphoneLock || settings.stopOnDisconnect)) {
                pause()
            }
        }
    }

    val state: StateFlow<NoiseState> = _state
    val settings: StateFlow<NoiseSettings> get() = store.settings

    /** Called when the state changes, e.g. to redraw the widget. */
    var onChanged: (() -> Unit)? = null

    init {
        _state.value = _state.value.copy(headphonesConnected = Headphones.connected(audio))
        audio.registerAudioDeviceCallback(devices, handler)
        scope.launch(Dispatchers.Main) { reload() }
    }

    /** True while the lock is on and no headphones are connected: Play does nothing. */
    fun lockedOut(): Boolean = store.settings.value.headphoneLock && !_state.value.headphonesConnected

    /** The folder picked with Android's folder picker: kept across restarts. False when it can't be kept. */
    fun chooseFolder(uri: Uri): Boolean {
        try {
            context.contentResolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION)
        } catch (e: SecurityException) {
            return false
        }

        pause()
        store.save(store.settings.value.copy(folderUri = uri.toString(), lastFile = null))
        release()
        reload()
        return true
    }

    fun toggle() = if (_state.value.playing) pause() else play()

    fun play() {
        lastPlayRefused = false
        if (files.isEmpty()) {
            reload()
        }

        if (index < 0) {
            return
        }

        if (lockedOut()) {
            lastPlayRefused = true
            refuseForLock()
            return
        }

        if (store.settings.value.systemControls && !requestFocus()) {
            return
        }

        val current = player
        if (current != null && preparedIndex == index) {
            current.start()
            playing(true)
        } else {
            prepare(index, startWhenReady = true)
        }
    }

    fun pause() {
        player?.takeIf { runCatching { it.isPlaying }.getOrDefault(false) }?.pause()
        pausedForFocus = false
        abandonFocus()
        playing(false)
    }

    fun next() = move(NoisePlaylist.next(index, files.size))

    fun previous() = move(NoisePlaylist.previous(index, files.size))

    fun setVolume(volume: Int) {
        val level = volume.coerceIn(0, 100)
        store.save(store.settings.value.copy(volume = level))
        player?.setVolume(level / 100f, level / 100f)
    }

    /** Volume down or up by [step] (the widget's − and +). */
    fun nudgeVolume(step: Int) {
        setVolume(store.settings.value.volume + step)
        onChanged?.invoke()
    }

    fun setWidgetVolume(on: Boolean) {
        store.save(store.settings.value.copy(widgetVolume = on))
        onChanged?.invoke()
    }

    fun setRepeat(repeat: Boolean) {
        store.save(store.settings.value.copy(repeat = repeat))
        player?.isLooping = repeat
    }

    /** Media controls on or off; takes effect at once (a playing sound keeps playing). */
    fun setSystemControls(on: Boolean) {
        store.save(store.settings.value.copy(systemControls = on))
        if (on) {
            if (_state.value.playing) requestFocus()
            updateSession()
        } else {
            abandonFocus()
            session?.release()
            session = null
        }
        notifyService()
    }

    /** The lock on the player: on while playing through the speaker pauses it at once. */
    fun setHeadphoneLock(on: Boolean) {
        store.save(store.settings.value.copy(headphoneLock = on))
        if (on && _state.value.playing && !_state.value.headphonesConnected) {
            pause()
        } else {
            onChanged?.invoke()
        }
    }

    fun setStopOnDisconnect(on: Boolean) {
        store.save(store.settings.value.copy(stopOnDisconnect = on))
    }

    fun setWidgetButton(on: Boolean) {
        store.save(store.settings.value.copy(widgetButton = on))
        onChanged?.invoke()
    }

    /** Reads the folder again (e.g. after files were added). */
    fun reload() {
        val folder = store.settings.value.folderUri
        files = folder?.let { NoisePlaylist.audioOnly(children(Uri.parse(it))) }.orEmpty()
        if (index !in files.indices) {
            index = NoisePlaylist.start(files, store.settings.value.lastFile)
        }
        publish()
    }

    private fun move(target: Int) {
        if (target < 0) {
            return
        }

        val wasPlaying = _state.value.playing
        index = target
        release()
        if (wasPlaying) {
            prepare(index, startWhenReady = true)
        } else {
            store.save(store.settings.value.copy(lastFile = files[index].name))
            publish()
        }
    }

    private fun prepare(target: Int, startWhenReady: Boolean) {
        release()
        val file = files.getOrNull(target) ?: return
        val settings = store.settings.value
        val created = MediaPlayer()
        try {
            created.setAudioAttributes(
                AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build(),
            )
            created.setDataSource(context, Uri.parse(file.uri))
            created.isLooping = settings.repeat
            created.setVolume(settings.volume / 100f, settings.volume / 100f)
            created.setOnCompletionListener { if (!store.settings.value.repeat) next() }
            created.setOnPreparedListener { ready ->
                preparedIndex = target
                // Headphones may have gone while it was preparing: never start through the speaker.
                if (startWhenReady && !lockedOut()) {
                    ready.start()
                    playing(true)
                } else {
                    playing(false)
                }
            }
            created.setOnErrorListener { _, _, _ ->
                playing(false)
                true
            }
            player = created
            store.save(settings.copy(lastFile = file.name))
            created.prepareAsync()
        } catch (e: Exception) {
            // The file can't be read (moved, or access lost): nothing plays.
            created.release()
            player = null
            playing(false)
        }
    }

    // The lock said no: the bar says "Connect headphones" for a few seconds.
    private fun listenForNoisy(on: Boolean) {
        if (on == noisyRegistered) {
            return
        }

        if (on) {
            ContextCompat.registerReceiver(context, noisy, IntentFilter(AudioManager.ACTION_AUDIO_BECOMING_NOISY), ContextCompat.RECEIVER_NOT_EXPORTED)
        } else {
            runCatching { context.unregisterReceiver(noisy) }
        }
        noisyRegistered = on
    }

    private fun refuseForLock() {
        _state.value = _state.value.copy(needsHeadphones = true)
        onChanged?.invoke()
        handler.removeCallbacks(clearHint)
        handler.postDelayed(clearHint, HINT_MILLIS)
    }

    private fun headphones(connected: Boolean) {
        if (connected == _state.value.headphonesConnected) {
            return
        }

        _state.value = _state.value.copy(headphonesConnected = connected, needsHeadphones = _state.value.needsHeadphones && !connected)
        onChanged?.invoke()
    }

    private fun release() {
        player?.release()
        player = null
        preparedIndex = -1
    }

    private fun playing(on: Boolean) {
        _state.value = _state.value.copy(playing = on)
        listenForNoisy(on || pausedForFocus)
        publish()
        notifyService()
    }

    private fun publish() {
        val file = files.getOrNull(index)
        _state.value = _state.value.copy(
            title = file?.let { NoisePlaylist.displayName(it.name) },
            hasFolder = store.settings.value.folderUri != null,
            fileCount = files.size,
        )
        updateSession()
        onChanged?.invoke()
    }

    // The foreground service keeps a playing sound alive; it stops itself once nothing plays.
    private fun notifyService() {
        val intent = Intent(context, NoiseService::class.java).setAction(NoiseService.ACTION_UPDATE)
        if (_state.value.playing) {
            try {
                ContextCompat.startForegroundService(context, intent)
            } catch (e: IllegalStateException) {
                // Started from the background (headphones connected) where Android refuses it; the pinned timeline
                // keeps the app, and so the sound, alive.
                android.util.Log.w("NoisePlayer", "noise service not started", e)
            }
        } else if (NoiseService.running) {
            context.startService(intent)
        }
    }

    private fun requestFocus(): Boolean {
        val request = focus ?: AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN)
            .setAudioAttributes(AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).setContentType(AudioAttributes.CONTENT_TYPE_MUSIC).build())
            .setOnAudioFocusChangeListener { change ->
                when (change) {
                    AudioManager.AUDIOFOCUS_LOSS, AudioManager.AUDIOFOCUS_LOSS_TRANSIENT -> if (_state.value.playing) {
                        pausedForFocus = change == AudioManager.AUDIOFOCUS_LOSS_TRANSIENT
                        player?.pause()
                        playing(false)
                    }
                    AudioManager.AUDIOFOCUS_GAIN -> if (pausedForFocus) {
                        pausedForFocus = false
                        // Headphones went while another app had the sound: stay paused.
                        if (lockedOut()) return@setOnAudioFocusChangeListener
                        player?.start()
                        playing(true)
                    }
                }
            }
            .build()
            .also { focus = it }
        return audio.requestAudioFocus(request) == AudioManager.AUDIOFOCUS_REQUEST_GRANTED
    }

    private fun abandonFocus() {
        focus?.let { audio.abandonAudioFocusRequest(it) }
    }

    // Android's media controls, only while "Use Android's media controls" is on.
    private fun updateSession() {
        if (!store.settings.value.systemControls) {
            return
        }

        val current = session ?: MediaSession(context, "AccountabiliBuddy noise").also { created ->
            created.setCallback(object : MediaSession.Callback() {
                override fun onPlay() = play()
                override fun onPause() = pause()
                override fun onSkipToNext() = next()
                override fun onSkipToPrevious() = previous()
            })
            session = created
        }
        val playingNow = _state.value.playing
        current.setPlaybackState(
            PlaybackState.Builder()
                .setActions(PlaybackState.ACTION_PLAY or PlaybackState.ACTION_PAUSE or PlaybackState.ACTION_PLAY_PAUSE or
                    PlaybackState.ACTION_SKIP_TO_NEXT or PlaybackState.ACTION_SKIP_TO_PREVIOUS)
                .setState(if (playingNow) PlaybackState.STATE_PLAYING else PlaybackState.STATE_PAUSED, PlaybackState.PLAYBACK_POSITION_UNKNOWN, 1f)
                .build(),
        )
        current.setMetadata(MediaMetadata.Builder().putString(MediaMetadata.METADATA_KEY_TITLE, _state.value.title ?: "Noise").build())
        current.isActive = true
    }

    /** The media session's token for the notification, while media controls are on. */
    fun sessionToken(): MediaSession.Token? = session?.sessionToken

    private fun children(tree: Uri): List<NoiseFile> = try {
        val treeId = DocumentsContract.getTreeDocumentId(tree)
        val childrenUri = DocumentsContract.buildChildDocumentsUriUsingTree(tree, treeId)
        val columns = arrayOf(DocumentsContract.Document.COLUMN_DOCUMENT_ID, DocumentsContract.Document.COLUMN_DISPLAY_NAME)
        val found = mutableListOf<NoiseFile>()
        context.contentResolver.query(childrenUri, columns, null, null, null)?.use { cursor ->
            while (cursor.moveToNext()) {
                val id = cursor.getString(0) ?: continue
                val name = cursor.getString(1) ?: continue
                found += NoiseFile(name, DocumentsContract.buildDocumentUriUsingTree(tree, id).toString())
            }
        }
        found
    } catch (e: Exception) {
        emptyList() // Access was lost or the folder is gone: choose it again.
    }

    private companion object {
        const val HINT_MILLIS = 3000L
    }
}
