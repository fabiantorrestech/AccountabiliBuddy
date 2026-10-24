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

/**
 * [order]: the essentials screen's order, as "folder:<id>" and "app:<package>" keys (arranged in Settings); shown
 * folders and visible essentials not in it follow, folders first, then A–Z.
 */
data class Essentials(val apps: List<EssentialApp> = emptyList(), val folders: List<AppFolder> = emptyList(), val order: List<String> = emptyList()) {
    /** The screen's rows in order: shown folders and visible essentials, as keys. */
    fun homeKeys(nameOf: (String) -> String): List<String> {
        val shown = folders.filter { it.shown }.map { folderKey(it.id) } +
            apps.filter { !it.hidden }.sortedBy { nameOf(it.packageName).lowercase() }.map { appKey(it.packageName) }
        return order.filter { it in shown } + shown.filter { it !in order }
    }

    companion object {
        fun folderKey(id: String) = "folder:$id"
        fun appKey(packageName: String) = "app:$packageName"
    }

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
    private val appContext = context.applicationContext
    private val prefs = context.getSharedPreferences("essentials", Context.MODE_PRIVATE)
    private val legacy = context.getSharedPreferences("emergency", Context.MODE_PRIVATE)
    private val _essentials = MutableStateFlow(read())
    val essentials: StateFlow<Essentials> = _essentials.asStateFlow()

    // LockMyPhone-Extended's "Accountabilibuddy" profile follows the essentials: sent when first used after a start, then on each change.
    init {
        if (useLockMyPhone) ProfileSync.send(appContext, _essentials.value)
    }

    /**
     * Whether AccountabiliBuddy uses LockMyPhone-Extended at all (on by default): the hardcore profile for each session
     * and its "Accountabilibuddy" profile kept in step with the essentials. Off = neither.
     */
    var useLockMyPhone: Boolean
        get() = prefs.getBoolean(KEY_USE_LMP, true)
        set(value) {
            prefs.edit().putBoolean(KEY_USE_LMP, value).apply()
            if (value) ProfileSync.send(appContext, _essentials.value)
        }

    /** The essentials screen on unlocking during a session on another device (on by default). */
    var onUnlockInSession: Boolean
        get() = prefs.getBoolean(KEY_IN_SESSION, true)
        set(value) = prefs.edit().putBoolean(KEY_IN_SESSION, value).apply()

    /** Where the Phone shortcut sits on the essentials screen: "right" (bottom right, the default), "left" or "list". */
    var phoneCorner: String
        get() = prefs.getString(KEY_PHONE, "right") ?: "right"
        set(value) = prefs.edit().putString(KEY_PHONE, value).apply()

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
        if (useLockMyPhone) ProfileSync.send(appContext, next)
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
                order = json["order"]?.jsonArray.orEmpty().map { it.jsonPrimitive.content },
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
        put("order", JsonArray(e.order.map { JsonPrimitive(it) }))
    }

    /** Moves a row of the essentials screen up (-1) or down (+1); [keys] is the screen's order as shown. */
    fun moveHome(keys: List<String>, key: String, by: Int) {
        val index = keys.indexOf(key)
        val target = index + by
        if (index < 0 || target !in keys.indices) return
        val moved = keys.toMutableList().apply { add(target, removeAt(index)) }
        update { it.copy(order = moved) }
    }

    /** Moves an app inside a folder up (-1) or down (+1). */
    fun moveInFolder(folderId: String, packageName: String, by: Int) = update { e ->
        e.copy(folders = e.folders.map { folder ->
            if (folder.id != folderId) return@map folder
            val index = folder.packages.indexOf(packageName)
            val target = index + by
            if (index < 0 || target !in folder.packages.indices) folder
            else folder.copy(packages = folder.packages.toMutableList().apply { add(target, removeAt(index)) })
        })
    }

    companion object {
        private const val KEY_DATA = "data"
        private const val KEY_IN_SESSION = "onUnlockInSession"
        private const val KEY_ICONS = "showIcons"
        private const val KEY_PHONE = "phoneCorner"
        private const val KEY_HARDCORE = "hardcoreProfile"
        private const val KEY_USE_LMP = "useLockMyPhone"

        /** Google's phone, messages, maps, clock, calendar and authenticator; banking apps are added in Settings. */
        val DEFAULT_APPS = setOf(
            "com.google.android.dialer", "com.google.android.apps.messaging", "com.google.android.apps.maps",
            "com.google.android.deskclock", "com.google.android.calendar", "com.google.android.apps.authenticator2",
        )
    }
}
