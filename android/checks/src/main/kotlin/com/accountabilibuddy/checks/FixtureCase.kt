package com.accountabilibuddy.checks

import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonPrimitive

/** One fixture case: the C# side's [input] and what the C# rule produced from it ([expected]). */
class FixtureCase(val name: String, val input: JsonObject, val expected: JsonElement, private val expectedWire: JsonElement? = null) {
    /**
     * The PC's own bytes of the result (what its serializer wrote), when the fixture carries them: a text, or per part of an object [expected].
     * The fixture file re-escapes every string ("+" as +, a time too), so only these texts tell a known time (written straight) from
     * any other string (escaped).
     */
    fun wire(part: String? = null): String? {
        val element = if (part == null) expectedWire else (expectedWire as? JsonObject)?.get(part)
        return (element as? kotlinx.serialization.json.JsonPrimitive)?.takeIf { it.isString }?.content
    }

    /** An input value as text (the sync file's text form). */
    fun text(key: String): String = input.getValue(key).jsonPrimitive.content
}
