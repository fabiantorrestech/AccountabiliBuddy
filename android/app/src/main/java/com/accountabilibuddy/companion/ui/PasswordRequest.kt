package com.accountabilibuddy.companion.ui

import android.net.Uri

/** A picked backup that needs its password before it can be imported. */
data class PasswordRequest(val uri: Uri, val wrongPassword: Boolean = false)
