package com.accountabilibuddy.shared.goals

/**
 * ADR 79 forward compatibility: a goal enum value this app doesn't know, written by a newer app ([GoalJson]); the PC's
 * `GoalNewerValueException`. A goal list keeps the item that holds it as written ([GoalNewerItem]) instead of refusing the whole
 * list; any other [IllegalArgumentException] from [GoalJson] still means the JSON is damaged.
 */
class GoalNewerValueException(message: String) : IllegalArgumentException(message)
