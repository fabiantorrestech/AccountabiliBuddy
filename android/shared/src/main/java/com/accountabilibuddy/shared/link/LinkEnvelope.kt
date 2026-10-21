package com.accountabilibuddy.shared.link

import kotlinx.serialization.json.JsonElement

/** One message: its type (as in the PC's LinkMessages) and its body, if any. */
data class LinkEnvelope(val type: String, val body: JsonElement?)
