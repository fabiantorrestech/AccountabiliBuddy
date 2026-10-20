package com.accountabilibuddy.companion.curbox

import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.ServiceConnection
import android.os.IBinder
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.intOrNull
import neth.iecal.curbox.api.ICurboxApi
import kotlin.coroutines.resume

/**
 * Talks to Curbox through its public API (a bound service on the phone; the user allows this app once in a
 * Curbox dialog). Read only for now: its status, today's screen time and its groups. Curbox is found by the
 * API's action, not a fixed package (debug and release builds differ).
 */
class CurboxClient(private val context: Context) {
    /** Curbox's package when it's installed with the API, else null. */
    fun findPackage(): String? = context.packageManager.resolveService(Intent(BIND_ACTION), 0)?.serviceInfo?.packageName

    /** The intent that shows Curbox's "allow this app" dialog (launch it for a result). */
    fun permissionIntent(packageName: String): Intent = Intent(PERMISSION_ACTION).setPackage(packageName)

    suspend fun read(): CurboxStatus {
        val packageName = findPackage() ?: return CurboxStatus()
        return withTimeoutOrNull(5_000) {
            withBinding(packageName) { api ->
                val granted = api.isGranted
                if (!granted) {
                    return@withBinding CurboxStatus(installed = true, packageName = packageName, granted = false, apiVersion = api.apiVersion())
                }

                val status = parse(api.list("STATUS")) as? JsonObject
                val screen = parse(api.query("SCREENTIME_TODAY")) as? JsonObject
                val groups = listOf("FOCUS_GROUPS" to "Focus", "APP_BLOCKER_GROUPS" to "Apps", "KEYWORD_GROUPS" to "Keywords")
                    .flatMap { (kind, label) -> groups(parse(api.list(kind)) as? JsonArray, label) }

                CurboxStatus(
                    installed = true,
                    packageName = packageName,
                    granted = true,
                    apiVersion = api.apiVersion(),
                    focusActive = status?.get("focusActive")?.let { (it as? JsonPrimitive)?.booleanOrNull } ?: false,
                    focusGroup = (status?.get("focusGroup") as? JsonPrimitive)?.content?.takeIf { it.isNotEmpty() },
                    focusRemainingMinutes = (status?.get("focusRemainingMinutes") as? JsonPrimitive)?.intOrNull,
                    screenTimeMinutes = (screen?.get("screentime") as? JsonPrimitive)?.content?.toIntOrNull(),
                    groups = groups,
                )
            }
        } ?: CurboxStatus(installed = true, packageName = packageName, error = "Curbox didn't answer.")
    }

    private fun groups(array: JsonArray?, label: String): List<CurboxGroup> = array.orEmpty().mapNotNull { element ->
        val o = element as? JsonObject ?: return@mapNotNull null
        val count = listOf("apps", "websites", "keywords").sumOf { (o[it] as? JsonPrimitive)?.intOrNull ?: 0 }
        CurboxGroup(
            kind = label,
            id = (o["id"] as? JsonPrimitive)?.content ?: "",
            name = (o["name"] as? JsonPrimitive)?.content ?: "",
            active = (o["isActive"] as? JsonPrimitive)?.booleanOrNull,
            count = count,
        )
    }

    private fun parse(text: String?) = text?.let { runCatching { Json.parseToJsonElement(it) }.getOrNull() }

    /** Binds, runs [block] off the main thread, unbinds (also when the caller stops waiting for Curbox). */
    private suspend fun <T> withBinding(packageName: String, block: (ICurboxApi) -> T): T? {
        var connection: ServiceConnection? = null
        return try {
            val api = suspendCancellableCoroutine<ICurboxApi?> { continuation ->
                val c = object : ServiceConnection {
                    override fun onServiceConnected(name: ComponentName?, service: IBinder?) {
                        if (continuation.isActive) continuation.resume(ICurboxApi.Stub.asInterface(service))
                    }

                    override fun onServiceDisconnected(name: ComponentName?) = Unit

                    override fun onNullBinding(name: ComponentName?) {
                        if (continuation.isActive) continuation.resume(null)
                    }
                }
                connection = c
                val bound = context.bindService(Intent(BIND_ACTION).setPackage(packageName), c, Context.BIND_AUTO_CREATE)
                if (!bound && continuation.isActive) continuation.resume(null)
            }

            api?.let { withContext(Dispatchers.IO) { block(it) } }
        } catch (e: android.os.RemoteException) {
            null
        } finally {
            // Without this, a timeout left Curbox's service bound (and running) until the app was closed.
            connection?.let { runCatching { context.unbindService(it) } }
        }
    }

    companion object {
        const val BIND_ACTION = "neth.iecal.curbox.api.BIND"
        const val PERMISSION_ACTION = "neth.iecal.curbox.api.REQUEST_PERMISSION"
    }
}
