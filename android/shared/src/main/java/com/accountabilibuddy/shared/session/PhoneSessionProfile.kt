package com.accountabilibuddy.shared.session

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.put

/** One of Curbox's blocks a session profile turns on for the session ([kind] is a `BlockRef.CURBOX_*` value, [id] the group id or the blocker's fixed id). */
data class CurboxBlockRef(val kind: String, val id: String)

/**
 * A phone session profile (ADR 85, plan section 2, prefs `phoneSessionProfiles`): what a session on this phone turns on. Phone-local, never synced,
 * never in the QR; the editor is PS3b, the lock and enforcement seam that reads it is PS2c.
 *
 * @property lmpProfile The LockMyPhone-Extended profile the session asks for, null = none (AccountabiliBuddy then falls back to the global one, plan check B7b).
 * @property curboxFocusGroup The Curbox focus group the session starts, null = none (AccountabiliBuddy falls back to hard mode's group).
 * @property curboxBlocks Curbox's blocks turned on for the session; only the ones that were off are turned back off at the end.
 * @property distractingApps Package names whose time inside the session counts as distraction (the session log only, never synced, R14).
 */
data class PhoneSessionProfile(
    val id: String,
    val name: String,
    val lmpProfile: String? = null,
    val curboxFocusGroup: String? = null,
    val curboxBlocks: List<CurboxBlockRef> = emptyList(),
    val distractingApps: List<String> = emptyList(),
    val defaultMinutes: Int = 50,
    val noSetEnd: Boolean = false,
    val editedAt: Long = 0L,
) {
    /** The enforcement the essentials hook reads (the Essentials screen has no switch of its own on a profile: a LockMyPhone profile is the lock). */
    val enforcement: OwnSessionEnforcement.Profile
        get() = OwnSessionEnforcement.Profile(lmpProfile, essentialsOnUnlock = false, curboxFocusGroup = curboxFocusGroup, curboxBlocks = curboxBlocks.isNotEmpty())

    /** What the profile turns on, for the ProductaBuddy lock scope (R11). A plain timer profile turns on nothing. */
    val use: PhoneSessionProfileUse get() = enforcement.use
}

/** The text of the `phoneSessionProfiles` preference. Unreadable text reads as null (the caller keeps what it had and never turns "can't read" into "none"). */
object PhoneSessionProfileJson {
    fun write(profiles: List<PhoneSessionProfile>): String = JsonArray(profiles.map(::one)).toString()

    fun read(text: String): List<PhoneSessionProfile>? = runCatching {
        (Json.parseToJsonElement(text) as JsonArray).map { readOne(it as JsonObject) }
    }.getOrNull()

    private fun one(p: PhoneSessionProfile): JsonObject = buildJsonObject {
        put("id", p.id)
        put("name", p.name)
        p.lmpProfile?.let { put("lmpProfile", it) }
        p.curboxFocusGroup?.let { put("curboxFocusGroup", it) }
        put("curboxBlocks", JsonArray(p.curboxBlocks.map { JsonArray(listOf(JsonPrimitive(it.kind), JsonPrimitive(it.id))) }))
        put("distractingApps", JsonArray(p.distractingApps.map { JsonPrimitive(it) }))
        put("defaultMinutes", p.defaultMinutes)
        put("noSetEnd", p.noSetEnd)
        put("editedAt", p.editedAt)
    }

    private fun readOne(json: JsonObject) = PhoneSessionProfile(
        id = text(json, "id")!!,
        name = text(json, "name")!!,
        lmpProfile = text(json, "lmpProfile"),
        curboxFocusGroup = text(json, "curboxFocusGroup"),
        curboxBlocks = (json["curboxBlocks"] as? JsonArray).orEmpty().map { pair ->
            val items = pair as JsonArray
            CurboxBlockRef((items[0] as JsonPrimitive).content, (items[1] as JsonPrimitive).content)
        },
        distractingApps = (json["distractingApps"] as? JsonArray).orEmpty().mapNotNull { (it as? JsonPrimitive)?.contentOrNull },
        defaultMinutes = (json["defaultMinutes"] as? JsonPrimitive)?.intOrNull ?: 50,
        noSetEnd = (json["noSetEnd"] as? JsonPrimitive)?.booleanOrNull == true,
        editedAt = (json["editedAt"] as? JsonPrimitive)?.contentOrNull?.toLongOrNull() ?: 0L,
    )

    private fun text(json: JsonObject, key: String): String? = (json[key] as? JsonPrimitive)?.takeIf { it.isString }?.contentOrNull
}
