package com.accountabilibuddy.checks

/** The Kotlin side of one fixture: runs each case through the phone's twin of the C# rule. */
interface FixtureCheck {
    /** The fixture folder's area and the file's name it reads (`<area>/<name>.json`). */
    val area: String
    val name: String

    fun run(fixture: FixtureFile, report: CheckReport)
}
