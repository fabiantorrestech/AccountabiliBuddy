package com.accountabilibuddy.companion.session

/** Seals the phone session files' text at rest (the Keystore in the app, a plain AES key in the checks). */
interface PhoneSessionSealer {
    fun seal(text: String): String

    /** Null when [sealed] can't be opened (the key is gone, or the text is damaged). */
    fun open(sealed: String): String?
}
