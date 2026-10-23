package com.accountabilibuddy.companion.noise

/** The noise player's playlist rules, as on the PC: sound files by name, starting at the last one played, wrapping around. */
object NoisePlaylist {
    private val SOUND_EXTENSIONS = setOf("mp3", "m4a", "aac", "ogg", "oga", "opus", "wav", "flac", "mp4", "webm", "wma")

    /** The sound files only, sorted by name (case ignored). */
    fun audioOnly(files: List<NoiseFile>): List<NoiseFile> =
        files.filter { it.name.substringAfterLast('.', "").lowercase() in SOUND_EXTENSIONS }.sortedBy { it.name.lowercase() }

    /** Where to start: the file played last, else the first; -1 for none. */
    fun start(files: List<NoiseFile>, lastName: String?): Int =
        if (files.isEmpty()) -1 else files.indexOfFirst { it.name == lastName }.takeIf { it >= 0 } ?: 0

    fun next(index: Int, count: Int): Int = if (count == 0) -1 else (index + 1) % count

    fun previous(index: Int, count: Int): Int = if (count == 0) -1 else (index - 1 + count) % count

    /** How a file is shown: its name without the extension. */
    fun displayName(name: String): String = name.substringBeforeLast('.')
}
