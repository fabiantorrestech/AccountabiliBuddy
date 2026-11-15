package com.accountabilibuddy.companion.essentials

import java.net.URI

/** Which addresses the LockMyPhone-Extended download may be fetched from, the first and every redirect hop (ADR 74). Pure. */
object RedirectRules {
    /** GitHub's release page and its asset hosts (release-assets is where an asset download redirects now, objects the older one); nothing else is followed. */
    val ALLOWED_HOSTS: Set<String> = setOf("github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com")

    /** True for an https address on an allowed host, with no user info and the default port. */
    fun allowed(url: String): Boolean {
        val uri = runCatching { URI(url) }.getOrNull() ?: return false
        val host = uri.host?.lowercase() ?: return false
        return uri.scheme.equals("https", ignoreCase = true) && uri.userInfo == null && (uri.port == -1 || uri.port == 443) && host in ALLOWED_HOSTS
    }

    /** Resolves a redirect's Location against the current address; null when it can't be read. */
    fun resolve(current: String, location: String?): String? =
        location?.takeIf { it.isNotBlank() }?.let { runCatching { URI(current).resolve(it).toString() }.getOrNull() }
}
