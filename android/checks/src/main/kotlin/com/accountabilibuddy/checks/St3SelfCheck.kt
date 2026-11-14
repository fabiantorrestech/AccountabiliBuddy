package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.Goal
import com.accountabilibuddy.shared.goals.GoalAppMatch
import com.accountabilibuddy.shared.goals.GoalAppMatchKind
import com.accountabilibuddy.shared.goals.GoalAppRow
import com.accountabilibuddy.shared.goals.GoalCounting
import com.accountabilibuddy.shared.goals.GoalDerivedWriter
import com.accountabilibuddy.shared.goals.GoalDrafts
import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalEntrySource
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalKind
import com.accountabilibuddy.shared.goals.GoalPeriod
import com.accountabilibuddy.shared.goals.GoalScreenTimeDerived
import com.accountabilibuddy.shared.goals.GoalScreenTimeEditor
import com.accountabilibuddy.shared.goals.GoalSeeds
import com.accountabilibuddy.shared.goals.GoalSourceData
import com.accountabilibuddy.shared.goals.GoalSourceRule
import com.accountabilibuddy.shared.goals.GoalSourceType
import com.accountabilibuddy.shared.goals.GoalTarget
import com.accountabilibuddy.shared.goals.GoalUnit
import java.io.File
import java.time.LocalDate
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.ZoneOffset
import java.util.UUID

/**
 * ST3 (phone screen time v2): counting per package on fake usage rows, the device picker, no churn, the Usage-access gaps, and nothing
 * private in what goes out. Pure functions over [GoalScreenTimeDerived] and [GoalScreenTimeEditor]; no device, no Usage access.
 */
object St3SelfCheck {
    private val PHONE = UUID.fromString("aaaaaaaa-0000-0000-0000-000000000001")
    private val PC = UUID.fromString("bbbbbbbb-0000-0000-0000-000000000002")
    private val OTHER_PHONE = UUID.fromString("cccccccc-0000-0000-0000-000000000003")
    private val T0 = OffsetDateTime.of(2026, 11, 14, 9, 0, 0, 0, ZoneOffset.UTC)
    private val DAY = LocalDate.of(2026, 11, 14)

    // Seeded private text: neither may appear in anything that is sent.
    private const val SECRET_PACKAGE = "com.secret.privateapp"
    private const val SECRET_NAME = "SecretPrivateApp"

    fun run(report: CheckReport) {
        counting(report)
        devices(report)
        noChurn(report)
        names(report)
        editor(report)
        sums(report)
    }

    private fun row(pkg: String, label: String, minutes: Double) = GoalAppRow(pkg, label, null, minutes)

    private fun rule(vararg apps: GoalAppMatch, devices: List<UUID>? = null, all: Boolean = false, createdOn: UUID? = PHONE, automatic: Boolean = true) =
        GoalSourceRule(UUID.fromString("88888888-0000-0000-0000-000000000001"), GoalSourceType.ScreenTime, automatic = automatic, apps = apps.toList(), devices = devices, allDevices = all, createdOn = createdOn)

    private fun goal(rule: GoalSourceRule, id: Int = 1) = Goal(
        UUID.fromString("99999999-0000-0000-0000-00000000000$id"), GoalSeeds.WORK_AREA_ID, "Less phone", "phone", "#112233", GoalKind.Time,
        GoalTarget(GoalPeriod.Day, 60.0, GoalUnit.Minutes), listOf(rule), T0, 1.0, T0,
    )

    private val pkg = { text: String -> GoalAppMatch(GoalAppMatchKind.Package, text) }
    private val name = { text: String -> GoalAppMatch(GoalAppMatchKind.Name, text) }

    private val rows = listOf(
        row("com.google.android.youtube", "YouTube", 40.5),
        row("com.instagram.android", "Instagram", 20.2),
        row("com.android.chrome", "Chrome", 10.0),
    )

    private fun counting(report: CheckReport) {
        fun minutes(rule: GoalSourceRule) = GoalScreenTimeDerived.minutes(goal(rule), PHONE, rows)
        report.check("st3/count: one package counts its own row (whole minutes)", minutes(rule(pkg("com.instagram.android"))) == 20.0)
        report.check("st3/count: a package and a name add up (60.7 -> 60)", minutes(rule(pkg("com.instagram.android"), name("youtube"))) == 60.0)
        report.check("st3/count: a row matched by a package and a name counts once", minutes(rule(pkg("com.instagram.android"), name("Instagram"))) == 20.0)
        report.check("st3/count: a package is exact (case and prefix)", minutes(rule(pkg("com.Instagram.android"), pkg("com.instagram"))) == 0.0)
        report.check(
            "st3/count: a process and a website never count on the phone",
            minutes(rule(GoalAppMatch(GoalAppMatchKind.Process, "chrome.exe"), GoalAppMatch(GoalAppMatchKind.Website, "youtube.com"))) == 0.0,
        )
        report.check("st3/count: a name is case-insensitive and exact", minutes(rule(name("CHROME"))) == 10.0 && minutes(rule(name("Chrom"))) == 0.0)
        report.check("st3/count: a rule that is off counts nothing", GoalScreenTimeDerived.counting(listOf(goal(rule(pkg("com.instagram.android"), automatic = false))), PHONE).isEmpty())
        report.check("st3/count: a source with no entries counts nothing", GoalScreenTimeDerived.counting(listOf(goal(rule())), PHONE).isEmpty())
        val two = GoalSourceRule(UUID.randomUUID(), GoalSourceType.ScreenTime, apps = listOf(pkg("com.instagram.android")), createdOn = PHONE)
        val twoGoal = goal(rule(name("YouTube"))).copy(sources = listOf(rule(name("YouTube")), two))
        report.check("st3/count: two rules of one goal share rows, a row once", GoalScreenTimeDerived.minutes(twoGoal, PHONE, rows) == 60.0)

        val byDay = mapOf(DAY to rows, DAY.minusDays(1) to listOf(row("com.instagram.android", "Instagram", 5.0)), DAY.minusDays(2) to emptyList())
        val out = GoalScreenTimeDerived.readings(listOf(goal(rule(pkg("com.instagram.android")))), PHONE, byDay)
        report.check(
            "st3/count: a reading per day with rows, none for a day without (no data is not a zero)",
            out.readings.map { it.day to it.value }.toSet() == setOf(DAY to 20.0, DAY.minusDays(1) to 5.0) && out.covered.isEmpty(),
        )
        val zero = GoalScreenTimeDerived.readings(listOf(goal(rule(pkg("com.not.used")))), PHONE, mapOf(DAY to rows))
        report.check("st3/count: phone use that matched nothing is a real 0", zero.readings.single().value == 0.0)
        val paused = GoalScreenTimeDerived.readings(listOf(goal(rule(pkg("com.instagram.android"))).copy(deletedAt = T0)), PHONE, mapOf(DAY to rows))
        report.check("st3/count: a deleted goal is not counted", paused.readings.isEmpty())
        report.check("st3/count: Deep work never counts screen time", GoalScreenTimeDerived.counting(listOf(GoalSeeds.deepWork(T0)), PHONE).isEmpty())
    }

    private fun devices(report: CheckReport) {
        val base = GoalScreenTimeEditor.newRule(UUID.randomUUID(), PHONE)
        report.check("st3/devices: a new rule is stamped with this phone and counts here only", base.createdOn == PHONE && GoalScreenTimeEditor.selected(base, PHONE, listOf(PC)) == setOf(PHONE))
        val withPc = GoalScreenTimeEditor.withDevice(base, PC, true, PHONE, listOf(PC))
        report.check("st3/devices: a PC can be ticked (listed, this phone first, createdOn kept)", withPc.devices == listOf(PHONE, PC) && withPc.createdOn == PHONE && !withPc.allDevices)
        val pcOnly = GoalScreenTimeEditor.withDevice(withPc, PHONE, false, PHONE, listOf(PC))
        report.check("st3/devices: this phone can be unticked while another counts", pcOnly.devices == listOf(PC) && !GoalAppMatching_countsOn(pcOnly, PHONE) && GoalAppMatching_countsOn(pcOnly, PC))
        val last = GoalScreenTimeEditor.withDevice(pcOnly, PC, false, PHONE, listOf(PC))
        report.check("st3/devices: the last device can't be unticked", last == pcOnly)
        val all = rule(pkg("com.instagram.android"), all = true, createdOn = PC)
        report.check("st3/devices: all devices counts here too; unticking one from all lists the rest", GoalAppMatching_countsOn(all, PHONE) && GoalScreenTimeEditor.withDevice(all, PC, false, PHONE, listOf(PC, OTHER_PHONE)).devices == listOf(PHONE, OTHER_PHONE))
        val notHere = goal(rule(pkg("com.instagram.android"), devices = listOf(PC)))
        report.check(
            "st3/devices: a goal that doesn't list this phone is neither read nor written here",
            GoalScreenTimeDerived.counting(listOf(notHere), PHONE).isEmpty() && GoalScreenTimeDerived.readings(listOf(notHere), PHONE, mapOf(DAY to rows)).readings.isEmpty(),
        )
        val old = rule(pkg("com.instagram.android"), createdOn = null)
        report.check("st3/devices: a rule written before devices were recorded counts here", GoalScreenTimeDerived.counting(listOf(goal(old)), PHONE).isNotEmpty())
        val other = rule(pkg("com.instagram.android"), createdOn = PC)
        report.check("st3/devices: a rule made on a PC with no list counts there only", GoalScreenTimeDerived.counting(listOf(goal(other)), PHONE).isEmpty())
        report.check("st3/devices: the flag lets other devices be ticked (ADR 79 accepted)", GoalScreenTimeEditor.OTHER_DEVICES_ENABLED)
        // S2: other devices are ticked only while goals sync is on and the phone is in a group (the sheet reads both; scanned, it is Compose).
        val sheet = File("../app/src/main/java/com/accountabilibuddy/companion/ui/goals/ScreenTimeSourceSheet.kt").takeIf { it.exists() }?.readText().orEmpty()
        report.check("st3/devices: the sheet greys other devices without Sync task lists or a group", sheet.contains("shareTaskLists && groupKey != null") && sheet.contains("OTHER_DEVICES_ENABLED && syncOn"))
    }

    private fun GoalAppMatching_countsOn(rule: GoalSourceRule, device: UUID) = com.accountabilibuddy.shared.goals.GoalAppMatching.countsOn(rule, device)

    private fun noChurn(report: CheckReport) {
        val goals = listOf(goal(rule(pkg("com.instagram.android"))))
        val first = GoalScreenTimeDerived.readings(goals, PHONE, mapOf(DAY to rows, DAY.minusDays(1) to listOf(row("com.instagram.android", "Instagram", 5.0))))
        val written = GoalDerivedWriter.changes(emptyList(), PHONE, first.readings, first.covered, T0)
        report.check("st3/churn: the first run writes one entry per day, with this phone's id and the screen-time source", written.size == 2 && written.all { it.deviceId == PHONE && it.source == GoalEntrySource.ScreenTime })
        val again = GoalDerivedWriter.changes(written, PHONE, first.readings, first.covered, T0.plusMinutes(5))
        report.check("st3/churn: a second run with the same numbers writes nothing", again.isEmpty())
        val grown = GoalScreenTimeDerived.readings(goals, PHONE, mapOf(DAY to listOf(row("com.instagram.android", "Instagram", 20.9))))
        report.check("st3/churn: seconds inside a minute change nothing (whole minutes)", GoalDerivedWriter.changes(written, PHONE, grown.readings, grown.covered, T0.plusMinutes(10)).isEmpty())
        val minute = GoalScreenTimeDerived.readings(goals, PHONE, mapOf(DAY to listOf(row("com.instagram.android", "Instagram", 21.0))))
        val changed = GoalDerivedWriter.changes(written, PHONE, minute.readings, minute.covered, T0.plusMinutes(11))
        report.check("st3/churn: a new whole minute updates that entry in place (same id)", changed.size == 1 && changed[0].value == 21.0 && written.any { it.id == changed[0].id })
        // No Usage access / records gone: no readings, so nothing is written and nothing is removed.
        val none = GoalScreenTimeDerived.readings(goals, PHONE, emptyMap())
        report.check("st3/churn: no rows (no access) writes nothing and removes nothing", none.readings.isEmpty() && GoalDerivedWriter.changes(written, PHONE, none.readings, none.covered, T0.plusDays(1)).isEmpty())
        val gap = GoalScreenTimeDerived.readings(goals, PHONE, mapOf(DAY to rows, DAY.minusDays(1) to emptyList()))
        report.check("st3/churn: a past day whose records are gone keeps its earlier value", GoalDerivedWriter.changes(written, PHONE, gap.readings, gap.covered, T0.plusDays(1)).none { !it.isLive || it.day == DAY.minusDays(1) })
        val foreign = GoalEntry(UUID.randomUUID(), goals[0].id, DAY, null, 99.0, GoalEntrySource.ScreenTime, T0, deviceId = PC)
        val withForeign = GoalDerivedWriter.changes(written + foreign, PHONE, minute.readings, minute.covered, T0.plusMinutes(12))
        report.check("st3/churn: another device's entry is never touched", withForeign.none { it.id == foreign.id })
        // S1: only today and yesterday are updated; an older day is written only when this phone has no live entry for it yet.
        val old = DAY.minusDays(5)
        val stored = GoalDerivedWriter.changes(emptyList(), PHONE, GoalScreenTimeDerived.readings(goals, PHONE, mapOf(old to listOf(row("com.instagram.android", "Instagram", 50.0)))).readings, emptyList(), T0)
        val partial = mapOf(old to listOf(row("com.instagram.android", "Instagram", 12.0)), DAY.minusDays(1) to listOf(row("com.instagram.android", "Instagram", 8.0)), DAY to listOf(row("com.instagram.android", "Instagram", 3.0)))
        val guarded = GoalScreenTimeDerived.readings(goals, PHONE, partial, DAY, stored)
        report.check("st3/churn: an older day with an entry is not lowered by a partial re-read (only today and yesterday update)", guarded.readings.map { it.day }.toSet() == setOf(DAY.minusDays(1), DAY))
        val backfill = GoalScreenTimeDerived.readings(goals, PHONE, partial, DAY, emptyList())
        report.check("st3/churn: an older day with no entry yet is written (backfill)", backfill.readings.size == 3 && backfill.readings.any { it.day == old && it.value == 12.0 })
        val tomb = stored.map { it.copy(deletedAt = T0) }
        report.check("st3/churn: a deleted older entry counts as none (backfilled)", GoalScreenTimeDerived.readings(goals, PHONE, partial, DAY, tomb).readings.any { it.day == old })
        // The in-memory readings for a goal being edited never double a stored entry.
        val memory = GoalScreenTimeDerived.unstored(goals, PHONE, mapOf(DAY to rows, DAY.minusDays(1) to listOf(row("com.instagram.android", "Instagram", 5.0))), written.filter { it.day == DAY }, T0)
        report.check("st3/churn: a reading is kept in memory only for a day with no stored entry", memory.size == 1 && memory[0].day == DAY.minusDays(1))
    }

    private fun names(report: CheckReport) {
        val goals = listOf(goal(rule(pkg(SECRET_PACKAGE), name(SECRET_NAME))))
        val seeded = listOf(row(SECRET_PACKAGE, SECRET_NAME, 33.0), row("com.other.app", "Other", 7.0))
        val result = GoalScreenTimeDerived.readings(goals, PHONE, mapOf(DAY to seeded))
        val entries = GoalDerivedWriter.changes(emptyList(), PHONE, result.readings, result.covered, T0)
        val sent = entries.joinToString("\n") { GoalJson.text(GoalJson.writeEntry(it)) } + "\n" + result.readings.joinToString("\n") { it.toString() }
        report.check("st3/names: the seeded app counted (33 min)", entries.single().value == 33.0)
        report.check("st3/names: no package or name in the written entries' JSON", entries.isNotEmpty() && !sent.contains(SECRET_PACKAGE) && !sent.contains(SECRET_NAME) && !sent.contains("secret", ignoreCase = true))
        val memory = GoalScreenTimeDerived.unstored(goals, PHONE, mapOf(DAY to seeded), emptyList(), T0).joinToString("\n") { GoalJson.text(GoalJson.writeEntry(it)) }
        report.check("st3/names: nor in the in-memory entries", memory.isNotEmpty() && !memory.contains(SECRET_PACKAGE) && !memory.contains(SECRET_NAME))

        // The new app code logs nothing and sends no text: scanned from the sources (they are not on the JVM).
        val files = listOf("goals/PhoneScreenTime.kt", "goals/PhoneGoalDerived.kt", "ui/goals/ScreenTimeSourceSheet.kt", "ui/ScreenTimeSetupPage.kt")
        val text = files.associateWith { File("../app/src/main/java/com/accountabilibuddy/companion/$it").takeIf { f -> f.exists() }?.readText().orEmpty() }
        report.check("st3/names: the screen-time files were found", text.values.all { it.isNotEmpty() })
        val logging = Regex("""\b(Log\.[dievw]\(|println\(|Timber\.|printStackTrace)""")
        report.check("st3/names: none of them logs", text.values.none { logging.containsMatchIn(it) })
        report.check("st3/names: the sheet and the setup page no longer say names never leave the phone", text.values.none { it.contains("never leave it") || it.contains("app names stay on the phone") })
        val screen = text["goals/PhoneScreenTime.kt"].orEmpty()
        report.check("st3/names: the main thread returns the kept rows before any lock", screen.indexOf("getMainLooper()) return lastRowsByDay") in 0 until screen.indexOf("synchronized(lock)") && !screen.contains("@Synchronized"))
    }

    private fun editor(report: CheckReport) {
        val base = GoalScreenTimeEditor.newRule(UUID.randomUUID(), PHONE)
        report.check("st3/editor: a source with no entry is a problem", GoalScreenTimeEditor.problem(base) != null)
        val kinds = listOf("com.foo.bar", "chrome.exe", "youtube.com", "Instagram", "my app").map { GoalScreenTimeEditor.add(base, it).apps!!.single().kind }
        report.check(
            "st3/editor: typing guesses the kind",
            kinds == listOf(GoalAppMatchKind.Package, GoalAppMatchKind.Process, GoalAppMatchKind.Website, GoalAppMatchKind.Name, GoalAppMatchKind.Name),
        )
        val added = GoalScreenTimeEditor.add(base, "Instagram")
        report.check("st3/editor: a blank text and the same entry twice add nothing", GoalScreenTimeEditor.add(added, "  ") == added && GoalScreenTimeEditor.add(added, "instagram") == added)
        report.check("st3/editor: the user's chosen kind wins over the guess", GoalScreenTimeEditor.add(base, "Instagram", GoalAppMatchKind.Package).apps!!.single().kind == GoalAppMatchKind.Package)
        val cycled = (1..4).map { n -> (1..n).fold(added) { rule, _ -> GoalScreenTimeEditor.cycleKind(rule, 0) }.apps!!.single().kind }
        report.check(
            "st3/editor: a chip tap goes through the four kinds and back",
            cycled == listOf(GoalAppMatchKind.Website, GoalAppMatchKind.Package, GoalAppMatchKind.Process, GoalAppMatchKind.Name),
        )
        report.check("st3/editor: remove takes the entry away; a bad index changes nothing", GoalScreenTimeEditor.remove(added, 0).apps!!.isEmpty() && GoalScreenTimeEditor.remove(added, 5) == added)
        val full = (1..GoalScreenTimeEditor.MAX_ENTRIES + 3).fold(base) { rule, n -> GoalScreenTimeEditor.add(rule, "App$n") }
        report.check("st3/editor: the list is capped", full.apps!!.size == GoalScreenTimeEditor.MAX_ENTRIES)
        val seen = setOf("com.instagram.android", "instagram")
        report.check(
            "st3/editor: notes - a seen entry has none, an unseen one says so, a PC kind says PCs only",
            GoalScreenTimeEditor.note(pkg("com.instagram.android"), seen) == null && GoalScreenTimeEditor.note(name("Instagram"), seen) == null &&
                GoalScreenTimeEditor.note(pkg("com.nope"), seen) == GoalScreenTimeEditor.NOT_SEEN && GoalScreenTimeEditor.note(name("Nope"), seen) == GoalScreenTimeEditor.NOT_SEEN &&
                GoalScreenTimeEditor.note(GoalAppMatch(GoalAppMatchKind.Process, "chrome.exe"), seen) == GoalScreenTimeEditor.PCS_ONLY &&
                GoalScreenTimeEditor.note(GoalAppMatch(GoalAppMatchKind.Website, "youtube.com"), seen) == GoalScreenTimeEditor.PCS_ONLY,
        )
        report.check(
            "st3/editor: the pick list filters by name or id, any case",
            GoalScreenTimeEditor.matchesTyped("insta", "Instagram", "com.instagram.android") && GoalScreenTimeEditor.matchesTyped("ANDROID", "Chrome", "com.android.chrome") && !GoalScreenTimeEditor.matchesTyped("zzz", "Chrome", "com.android.chrome") && GoalScreenTimeEditor.matchesTyped("", "x", "y"),
        )
        // Create stays closed until a screen-time source has an entry.
        val draft = GoalDrafts.newDraft(UUID.randomUUID(), GoalSeeds.WORK_AREA_ID).copy(name = "Less phone", sources = listOf(base))
        report.check("st3/editor: a draft with an empty screen-time source can't be created, with an entry it can (target aside)", GoalDrafts.sourcesProblem(draft) != null && GoalDrafts.sourcesProblem(draft.copy(sources = listOf(added))) == null)
    }

    private fun sums(report: CheckReport) {
        // The counting adds each chosen device's entry for a day (the GoalCounting rule, now fed by this phone's derived entries and a PC's).
        val rule = rule(pkg("com.instagram.android"), devices = listOf(PHONE, PC))
        val g = goal(rule)
        fun entry(device: UUID, value: Double) = GoalEntry(UUID.randomUUID(), g.id, DAY, null, value, GoalEntrySource.ScreenTime, T0, deviceId = device)
        val data = GoalSourceData(emptyList(), listOf(entry(PHONE, 20.0), entry(PC, 35.0), entry(OTHER_PHONE, 500.0)), ZoneId.of("UTC"))
        val day = GoalCounting.day(g, data, DAY, DAY)
        report.check("st3/sums: the phone's and the PC's minutes add up; a device the rule doesn't list is left out", day.value == 55.0)
    }
}
