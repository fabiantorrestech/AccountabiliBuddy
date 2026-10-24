package com.accountabilibuddy.companion.essentials

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.buildJsonArray
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put

/**
 * An essential app: always allowed (opening it never brings up the work card or counts as a pickup) and listed on the
 * essentials screen unless [hidden]. [parts] null = the whole app; else only those screens (activity class names),
 * e.g. Gemini's overlay but not its full window.
 */
data class EssentialApp(val packageName: String, val hidden: Boolean = false, val parts: Set<String>? = null)

/** A folder of apps: on the essentials screen as a folder that opens on a tap when [shown], else found only by searching its name. */
data class AppFolder(val id: String, val name: String, val packages: List<String>, val shown: Boolean = true)

data class Essentials(val apps: List<EssentialApp> = emptyList(), val folders: List<AppFolder> = emptyList()) {
    fun isAllowed(packageName: String?, activity: String? = null): Boolean {
        val app = apps.firstOrNull { it.packageName == packageName } ?: return false
        return app.parts == null || activity == null || activity in app.parts
    }
}

/**
 * The essentials (prefs "essentials"): the allowed apps, hidden or not, whole or in parts, and the folders; plus the
 * switches for when the essentials screen comes up and the LockMyPhone-Extended "hardcore" profile. On first use it
 * takes the old always-allowed list (prefs "emergency").
 */
class EssentialsStore(context: Context) {
    private val prefs = context.getSharedPreferences("essentials", Context.MODE_PRIVATE)
    private val legacy = context.getSharedPreferences("emergency", Context.MODE_PRIVATE)
    private val _essentials = MutableStateFlow(read())
    val essentials: StateFlow<Essentials> = _essentials.asStateFlow()

    /** The essentials screen on unlocking during a session on another device (on by default). */
    var onUnlockInSession: Boolean
        get() = prefs.getBoolean(KEY_IN_SESSION, true)
        set(value) = prefs.edit().putBoolean(KEY_IN_SESSION, value).apply()

    /** Icons next to the names (on by default). */
    var showIcons: Boolean
        get() = prefs.getBoolean(KEY_ICONS, true)
        set(value) = prefs.edit().putBoolean(KEY_ICONS, value).apply()

    /** The LockMyPhone-Extended profile to start for each session on another device; null = off. */
    var hardcoreProfile: String?
        get() = prefs.getString(KEY_HARDCORE, null)?.takeIf { it.isNotBlank() }
        set(value) = prefs.edit().putString(KEY_HARDCORE, value?.trim()).apply()

    fun isAllowed(packageName: String?, activity: String? = null): Boolean = _essentials.value.isAllowed(packageName, activity)

    fun update(change: (Essentials) -> Essentials) {
        val next = change(_essentials.value)
        _essentials.value = next
        prefs.edit().putString(KEY_DATA, write(next).toString()).apply()
    }

    fun setAllowed(packageName: String, on: Boolean) = update { e ->
        if (on) e.copy(apps = e.apps.filterNot { it.packageName == packageName } + EssentialApp(packageName))
        else e.copy(apps = e.apps.filterNot { it.packageName == packageName })
    }

    fun setHidden(packageName: String, hidden: Boolean) = update { e -> e.copy(apps = e.apps.map { if (it.packageName == packageName) it.copy(hidden = hidden) else it }) }

    fun setParts(packageName: String, parts: Set<String>?) = update { e -> e.copy(apps = e.apps.map { if (it.packageName == packageName) it.copy(parts = parts) else it }) }

    private fun read(): Essentials {
        val text = prefs.getString(KEY_DATA, null)
        if (text == null) {
            // First use: the old always-allowed apps become essentials.
            val old = legacy.getStringSet("allowedApps", null) ?: DEFAULT_APPS
            return Essentials(old.map { EssentialApp(it) })
        }

        return runCatching {
            val json = Json.parseToJsonElement(text) as JsonObject
            Essentials(
                apps = json["apps"]?.jsonArray.orEmpty().mapNotNull { element ->
                    val o = element as? JsonObject ?: return@mapNotNull null
                    EssentialApp(
                        o.getValue("package").jsonPrimitive.content,
                        (o["hidden"] as? JsonPrimitive)?.boolean ?: false,
                        (o["parts"] as? JsonArray)?.map { it.jsonPrimitive.content }?.toSet(),
                    )
                },
                folders = json["folders"]?.jsonArray.orEmpty().mapNotNull { element ->
                    val o = element as? JsonObject ?: return@mapNotNull null
                    AppFolder(
                        o.getValue("id").jsonPrimitive.content,
                        o.getValue("name").jsonPrimitive.content,
                        o["packages"]?.jsonArray.orEmpty().map { it.jsonPrimitive.content },
                        (o["shown"] as? JsonPrimitive)?.boolean ?: true,
                    )
                },
            )
        }.getOrDefault(Essentials())
    }

    private fun write(e: Essentials): JsonObject = buildJsonObject {
        put("apps", buildJsonArray {
            e.apps.forEach { app ->
                add(buildJsonObject {
                    put("package", app.packageName)
                    if (app.hidden) put("hidden", true)
                    app.parts?.let { parts -> put("parts", JsonArray(parts.map { JsonPrimitive(it) })) }
                })
            }
        })
        put("folders", buildJsonArray {
            e.folders.forEach { folder ->
                add(buildJsonObject {
                    put("id", folder.id)
                    put("name", folder.name)
                    put("packages", JsonArray(folder.packages.map { JsonPrimitive(it) }))
                    put("shown", folder.shown)
                })
            }
        })
    }

    companion object {
        private const val KEY_DATA = "data"
        private const val KEY_IN_SESSION = "onUnlockInSession"
        private const val KEY_ICONS = "showIcons"
        private const val KEY_HARDCORE = "hardcoreProfile"

        /** Google's phone, messages, maps, clock, calendar and authenticator; banking apps are added in Settings. */
        val DEFAULT_APPS = setOf(
            "com.google.android.dialer", "com.google.android.apps.messaging", "com.google.android.apps.maps",
            "com.google.android.deskclock", "com.google.android.calendar", "com.google.android.apps.authenticator2",
        )
    }
}
