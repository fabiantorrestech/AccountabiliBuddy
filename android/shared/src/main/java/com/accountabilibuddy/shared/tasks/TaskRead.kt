package com.accountabilibuddy.shared.tasks

import kotlinx.serialization.json.JsonObject

/**
 * One task part read from JSON ([TaskJson]): the entries this version can use, and the ones holding a value only a newer app knows
 * (a bin, a kind, a status), kept as written so a store can write them back. Newer entries never take part in a merge and are never shown.
 */
data class TaskRead<T>(val items: List<T>, val newer: List<JsonObject>)
