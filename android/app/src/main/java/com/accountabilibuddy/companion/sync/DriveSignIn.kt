package com.accountabilibuddy.companion.sync

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import com.google.android.gms.auth.api.identity.AuthorizationRequest
import com.google.android.gms.auth.api.identity.Identity
import com.google.android.gms.common.api.Scope
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlin.coroutines.resume

/** What starting the Drive sign-in gave: a token at once, a Google screen to show first, or a problem. */
sealed interface DriveSignInStep {
    data class Token(val token: String) : DriveSignInStep
    data class Ask(val intent: PendingIntent) : DriveSignInStep
    data class Failed(val message: String) : DriveSignInStep
}

/**
 * The phone's Google sign-in for Drive (ADR 26), through Play services' authorization client
 * (`com.google.android.gms:play-services-auth`, approved 2026-10-23): asks only for drive.file. Play services keeps
 * the grant and hands out fresh access tokens; the app keeps no refresh token. Needs an Android OAuth client in the
 * same Google Cloud project as the PC's (this app's package and signing key).
 */
class DriveSignIn(private val context: Context) {
    private val client get() = Identity.getAuthorizationClient(context)

    private fun request(): AuthorizationRequest = AuthorizationRequest.builder().setRequestedScopes(listOf(Scope(SCOPE))).build()

    /** From the screen: a token when access was already granted, else Google's consent screen to show. */
    suspend fun begin(): DriveSignInStep = suspendCancellableCoroutine { continuation ->
        client.authorize(request())
            .addOnSuccessListener { result ->
                val step = when {
                    result.hasResolution() -> result.pendingIntent?.let { DriveSignInStep.Ask(it) } ?: DriveSignInStep.Failed("Google couldn't show the sign-in.")
                    result.accessToken != null -> DriveSignInStep.Token(result.accessToken!!)
                    else -> DriveSignInStep.Failed("Google gave no access.")
                }
                continuation.resume(step)
            }
            .addOnFailureListener { continuation.resume(DriveSignInStep.Failed(FAILED)) }
    }

    /** The answer from Google's consent screen: the token, or null when it was refused or cancelled. */
    fun tokenFrom(data: Intent?): String? =
        data?.let { runCatching { client.getAuthorizationResultFromIntent(it).accessToken }.getOrNull() }

    /** In the background: a fresh token when access is still granted, null when the user must sign in again. */
    suspend fun accessToken(): String? = suspendCancellableCoroutine { continuation ->
        client.authorize(request())
            .addOnSuccessListener { result -> continuation.resume(if (result.hasResolution()) null else result.accessToken) }
            .addOnFailureListener { continuation.resume(null) }
    }

    companion object {
        const val SCOPE = "https://www.googleapis.com/auth/drive.file"
        const val FAILED = "Couldn't sign in. Check that this phone's Google client is set up (see the PC's Google setup guide)."
    }
}
