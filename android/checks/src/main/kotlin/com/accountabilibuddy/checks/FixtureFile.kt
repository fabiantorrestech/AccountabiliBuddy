package com.accountabilibuddy.checks

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.io.File

/**
 * One fixture file written by the C# checks: `<root>/<area>/<name>.json` =
 * `{ "area", "name", "source", "cases": [ { "name", "input": {...}, "expected": ... } ] }`.
 */
class FixtureFile(val area: String, val name: String, val source: String, val cases: List<FixtureCase>) {
    companion object {
        /** Every fixture file of [area] under [root], by name; empty when the C# checks haven't written any. */
        fun readArea(root: File, area: String): List<FixtureFile> =
            File(root, area).listFiles { file -> file.isFile && file.extension == "json" }
                .orEmpty()
                .sortedBy { it.name }
                .map { read(it) }

        // An input that is not an object (a text, a list) is handed over as {"value": input}.
        private fun objectOf(input: kotlinx.serialization.json.JsonElement): kotlinx.serialization.json.JsonObject =
            input as? kotlinx.serialization.json.JsonObject ?: kotlinx.serialization.json.JsonObject(mapOf("value" to input))

        private fun read(file: File): FixtureFile {
            val json = Json.parseToJsonElement(file.readText()).jsonObject
            val cases = json.getValue("cases").jsonArray.map { item ->
                val case = item.jsonObject
                FixtureCase(case.getValue("name").jsonPrimitive.content, objectOf(case.getValue("input")), case.getValue("expected"), case["expectedWire"])
            }
            return FixtureFile(
                json.getValue("area").jsonPrimitive.content,
                json.getValue("name").jsonPrimitive.content,
                json["source"]?.jsonPrimitive?.content ?: "",
                cases,
            )
        }
    }
}
