package com.accountabilibuddy.companion.ui.theme

import com.accountabilibuddy.shared.TokenVault
import org.json.JSONObject

/**
 * The place After sunset works from (ADR 49): a point and what to call it ("Berlin, BE, DE" or "Typed in"). Private: kept
 * sealed with [TokenVault] in the phone's "ui" preferences, never synced, logged or sent anywhere, and clearable in
 * Settings → Appearance. There is no location permission; the user types it or picks a city.
 */
data class AppearancePlace(val point: GeoPoint, val label: String) {
    /** The sealed text stored in preferences. */
    fun seal(): String = TokenVault.seal(
        JSONObject().put("lat", point.latitude).put("lon", point.longitude).put("label", label).toString(),
        PLACE_ALIAS,
    )

    companion object {
        /** Its own Keystore key, apart from the sync code's. */
        const val PLACE_ALIAS = "accountabilibuddy-appearance-place"

        /** The place from its sealed text, or null when it is missing or can't be opened (an unreadable place counts as none). */
        fun open(sealed: String?): AppearancePlace? {
            if (sealed == null) return null
            val json = TokenVault.open(sealed, PLACE_ALIAS) ?: return null
            return runCatching {
                val obj = JSONObject(json)
                val point = GeoPoint(obj.getDouble("lat"), obj.getDouble("lon"))
                if (point.isValid) AppearancePlace(point, obj.optString("label", "Saved place")) else null
            }.getOrNull()
        }
    }
}
