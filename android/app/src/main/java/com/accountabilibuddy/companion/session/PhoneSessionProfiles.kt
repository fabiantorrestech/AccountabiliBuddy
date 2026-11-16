package com.accountabilibuddy.companion.session

import android.content.Context
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.shared.session.OwnSessionEnforcement
import com.accountabilibuddy.shared.session.PhoneSessionProfile
import com.accountabilibuddy.shared.session.PhoneSessionProfileJson
import com.accountabilibuddy.shared.session.PhoneSessionRecord

/**
 * The phone session profiles (prefs `phoneSessionProfiles`, key `profiles`): phone-local, never synced. PS2c reads them (the lock scope, the essentials hook,
 * Curbox); the editor is PS3b. A change is refused while a real phone session runs (R11: the session profiles are locked in both modes).
 */
class PhoneSessionProfiles(private val app: CompanionApp) {
    private val prefs get() = app.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    /** The saved profiles; none when nothing is saved or the text can't be read (never a guess). */
    fun all(): List<PhoneSessionProfile> = prefs.getString(KEY, null)?.let(PhoneSessionProfileJson::read).orEmpty()

    fun byId(id: String): PhoneSessionProfile? = all().firstOrNull { it.id == id }

    /** True when saved. False, and nothing changed, while a real phone session runs. */
    @Synchronized
    fun replaceAll(profiles: List<PhoneSessionProfile>): Boolean {
        if (app.phoneSession.realSessionRunning()) return false
        prefs.edit().putString(KEY, PhoneSessionProfileJson.write(profiles)).apply()
        return true
    }

    /**
     * What [record]'s profile turns on (N6 of the PS6a review): the profile the session STARTED with, found by the record's id. The profiles are locked while
     * the session runs, so the saved one is the one it started with; a profile that is gone turns nothing on.
     */
    fun enforcementOf(record: PhoneSessionRecord): OwnSessionEnforcement.Profile = byId(record.profileId)?.enforcement ?: OwnSessionEnforcement.Profile.NONE

    private companion object {
        const val PREFS = "phoneSessionProfiles"
        const val KEY = "profiles"
    }
}
