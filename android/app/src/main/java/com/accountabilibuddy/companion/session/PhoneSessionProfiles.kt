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
     * What the Start sheet offers: the saved profiles, the built-in "Focus" (a plain timer that turns nothing on) first. A saved profile can never be mistaken
     * for it (its id is not a profile's id).
     */
    fun choices(): List<PhoneSessionProfile> = listOf(BUILT_IN) + all()

    /**
     * What [record]'s profile turns on (PS2c review S3, PS6a review N6): the copy the session took of its profile when it STARTED ([PhoneSessionRecord.snapshot]).
     * Never a look-up in the live store: no writer of the profiles can change a running session's locks.
     */
    fun enforcementOf(record: PhoneSessionRecord): OwnSessionEnforcement.Profile = record.snapshot.enforcement

    companion object {
        private const val PREFS = "phoneSessionProfiles"
        private const val KEY = "profiles"

        /** The built-in profile: a plain timer with This session's tasks. It is never saved; AccountabiliBuddy still adds the global locks to it (plan check B7b). */
        const val BUILT_IN_ID = "builtin-focus"

        val BUILT_IN = PhoneSessionProfile(id = BUILT_IN_ID, name = "Focus")
    }
}
