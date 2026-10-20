package com.accountabilibuddy.companion.link

import kotlinx.serialization.json.JsonElement

/** One message: its type ([LinkMessages]) and its body, if any. */
data class LinkEnvelope(val type: String, val body: JsonElement?)
