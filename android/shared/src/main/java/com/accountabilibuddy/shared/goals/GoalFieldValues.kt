package com.accountabilibuddy.shared.goals

/**
 * A goal part's value as text and as a copy (the PC's `GoalFieldValues`): the text is the part's JSON names with their raw JSON values,
 * built from the goal without its waiting change, so both platforms compare and break ties on the same bytes.
 */
object GoalFieldValues {
    /** The JSON names a part covers. */
    fun namesOf(field: GoalField): List<String> = when (field) {
        GoalField.Name -> listOf("name")
        GoalField.Look -> listOf("icon", "color")
        GoalField.Area -> listOf("areaId", "position")
        GoalField.Target -> listOf("kind", "target")
        GoalField.SecondTarget -> listOf("secondTarget", "trackTimeOnly")
        GoalField.Sources -> listOf("sources")
        GoalField.Pauses -> listOf("pauses")
        GoalField.ShowOnToday -> listOf("showOnToday")
        GoalField.ExtraTiles -> listOf("extraTiles")
        GoalField.Notes -> listOf("notes")
    }

    fun canonical(goal: Goal, field: GoalField): String = canonical(GoalJson.writeGoal(GoalSyncMerge.withoutPending(goal)), field)

    fun all(goal: Goal): Map<GoalField, String> {
        val json = GoalJson.writeGoal(GoalSyncMerge.withoutPending(goal))
        return GoalField.values().associateWith { canonical(json, it) }
    }

    /** [target] with [field] taken from [source]. */
    fun with(target: Goal, source: Goal, field: GoalField): Goal = when (field) {
        GoalField.Name -> target.copy(name = source.name)
        GoalField.Look -> target.copy(icon = source.icon, color = source.color)
        GoalField.Area -> target.copy(areaId = source.areaId, position = source.position)
        GoalField.Target -> target.copy(kind = source.kind, target = source.target)
        GoalField.SecondTarget -> target.copy(secondTarget = source.secondTarget, trackTimeOnly = source.trackTimeOnly)
        GoalField.Sources -> target.copy(sources = source.sources, newerSources = source.newerSources)
        GoalField.Pauses -> target.copy(pauses = source.pauses)
        GoalField.ShowOnToday -> target.copy(showOnToday = source.showOnToday)
        GoalField.ExtraTiles -> target.copy(extraTiles = source.extraTiles)
        GoalField.Notes -> target.copy(notes = source.notes)
    }

    private fun canonical(goal: kotlinx.serialization.json.JsonObject, field: GoalField): String =
        namesOf(field).joinToString("\n") { name -> name + "=" + (goal[name]?.let { GoalJson.text(it) } ?: "-") }
}
