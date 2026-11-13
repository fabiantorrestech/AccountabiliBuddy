package com.accountabilibuddy.shared.goals

/**
 * The parts of a goal that are stamped and merged on their own (ADR 79; the PC's `GoalField`). [jsonName] is the part's name in
 * `stamps`. Two things travel as one part where they belong together: the look (icon and colour), the place (area and position),
 * the kind and target, and Deep work's second target with its "Track time only".
 */
enum class GoalField(val jsonName: String) {
    Name("name"),
    Look("look"),
    Area("area"),
    Target("target"),
    SecondTarget("secondTarget"),
    Sources("sources"),
    Pauses("pauses"),
    ShowOnToday("showOnToday"),
    ExtraTiles("extraTiles"),
    Notes("notes"),
}
