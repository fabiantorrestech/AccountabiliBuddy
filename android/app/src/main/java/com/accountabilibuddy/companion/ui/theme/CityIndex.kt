package com.accountabilibuddy.companion.ui.theme

import android.content.Context
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.text.Normalizer

/**
 * Searches the bundled city list in memory (ADR 49). The list loads (off the main thread) on the first search and is let
 * go with [release] when the picker closes. Matches the start of the name first, then anywhere in it, ignoring case and
 * accents; the list is largest cities first, so bigger cities come first within each group.
 */
class CityIndex(private val context: Context) {
    private class Entry(val city: City, val folded: String)

    private var entries: List<Entry>? = null

    /** Finds cities for [query]; the first call reads and folds the list on a worker thread. */
    suspend fun search(query: String): List<City> = withContext(Dispatchers.Default) {
        val wanted = fold(query.trim())
        if (wanted.isEmpty()) return@withContext emptyList()
        match(entries ?: load().also { entries = it }, wanted)
    }

    /** Lets the list go; the next search loads it again. */
    fun release() {
        entries = null
    }

    private fun match(all: List<Entry>, wanted: String): List<City> {
        val results = ArrayList<City>(MAX_RESULTS)
        for (entry in all) {
            if (entry.folded.startsWith(wanted)) {
                results += entry.city
                if (results.size == MAX_RESULTS) return results
            }
        }
        for (entry in all) {
            if (!entry.folded.startsWith(wanted) && entry.folded.contains(wanted)) {
                results += entry.city
                if (results.size == MAX_RESULTS) break
            }
        }
        return results
    }

    private fun load(): List<Entry> {
        val loaded = ArrayList<Entry>(35_000)
        context.assets.open(LIST_ASSET).bufferedReader(Charsets.UTF_8).useLines { lines ->
            for (line in lines) {
                // geonameid, name, admin1 code, country code, latitude, longitude
                val parts = line.split('\t')
                if (parts.size < 6) continue
                val id = parts[0].toIntOrNull() ?: continue
                val latitude = parts[4].toDoubleOrNull() ?: continue
                val longitude = parts[5].toDoubleOrNull() ?: continue
                loaded += Entry(City(id, parts[1], parts[2], parts[3], GeoPoint(latitude, longitude)), fold(parts[1]))
            }
        }
        return loaded
    }

    // Lower case with accents taken off: "Zürich" is found by "zurich".
    private fun fold(text: String): String =
        Normalizer.normalize(text, Normalizer.Form.NFD).filter { Character.getType(it) != Character.NON_SPACING_MARK.toInt() }.lowercase()

    private companion object {
        const val MAX_RESULTS = 20
        const val LIST_ASSET = "cities.tsv"
    }
}
