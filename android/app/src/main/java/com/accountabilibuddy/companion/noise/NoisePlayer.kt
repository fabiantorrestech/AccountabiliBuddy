package com.accountabilibuddy.companion.noise

import android.content.Context
import android.content.Intent
import android.media.AudioAttributes
import android.media.AudioFocusRequest
import android.media.AudioManager
import android.media.MediaMetadata
import android.media.MediaPlayer
import android.media.session.MediaSession
import android.media.session.PlaybackState
import android.net.Uri
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
 * [NoiseService] keeps it alive in the foreground. Use from the main thread.
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

    val state: StateFlow<NoiseState> = _state
    val settings: StateFlow<NoiseSettings> get() = store.settings

    /** Called when the state changes, e.g. to redraw the widget. */
    var onChanged: (() -> Unit)? = null

    init {
        scope.launch(Dispatchers.Main) { reload() }
    }

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
        if (files.isEmpty()) {
            reload()
        }

        if (index < 0) {
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
                if (startWhenReady) {
                    ready.start()
                    playing(true)
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

    private fun release() {
        player?.release()
        player = null
        preparedIndex = -1
    }

    private fun playing(on: Boolean) {
        _state.value = _state.value.copy(playing = on)
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
            ContextCompat.startForegroundService(context, intent)
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
}
