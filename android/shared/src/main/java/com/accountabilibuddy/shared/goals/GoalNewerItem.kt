package com.accountabilibuddy.shared.goals

import kotlinx.serialization.json.JsonObject

/**
 * An item of a goal list kept as written because it holds a value this app doesn't know (a goal kind, source type, entry source,
 * reminder day, review choice of a newer app; ADR 79 forward compatibility): never counted or shown, never dropped, written back at
 * its place ([GoalJson.writeItems]); the PC's `GoalNewerItem`. May hold the user's private text: sealed at rest, never logged.
 *
 * @property index Its place in the list as read.
 * @property json The item as read.
 */
data class GoalNewerItem(val index: Int, val json: JsonObject)
