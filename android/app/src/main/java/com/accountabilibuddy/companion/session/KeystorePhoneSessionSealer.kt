package com.accountabilibuddy.companion.session

import com.accountabilibuddy.shared.TokenVault

/** The phone session files' sealing: AES-GCM with a key in the Android Keystore that never leaves it (like the task store and the goals). */
class KeystorePhoneSessionSealer : PhoneSessionSealer {
    override fun seal(text: String): String = TokenVault.seal(text, ALIAS)

    override fun open(sealed: String): String? = TokenVault.open(sealed, ALIAS)

    private companion object {
        const val ALIAS = "accountabilibuddy-phone-session"
    }
}
