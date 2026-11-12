package com.accountabilibuddy.checks

import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonPrimitive

/** One fixture case: the C# side's [input] and what the C# rule produced from it ([expected]). */
class FixtureCase(val name: String, val input: JsonObject, val expected: JsonElement) {
    /** An input value as text (the sync file's text form). */
    fun text(key: String): String = input.getValue(key).jsonPrimitive.content
}
