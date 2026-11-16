package com.accountabilibuddy.checks

import com.accountabilibuddy.companion.guidance.GuidanceMode
import com.accountabilibuddy.companion.guidance.GuidancePending
import com.accountabilibuddy.companion.guidance.GuidanceRules
import com.accountabilibuddy.companion.guidance.GuidanceState
import com.accountabilibuddy.companion.pause.PauseRules
import com.accountabilibuddy.companion.pause.PauseState
import com.accountabilibuddy.companion.session.PhoneSessionStart
import com.accountabilibuddy.shared.session.CurboxBlockRef
import com.accountabilibuddy.shared.session.CurboxSessionBlocksRule
import com.accountabilibuddy.shared.session.CurboxSessionBlocksRule.Action
import com.accountabilibuddy.shared.session.CurboxSessionBlocksRule.Memory
import com.accountabilibuddy.shared.session.EndCostTerms
import com.accountabilibuddy.shared.session.OwnSessionEnforcement
import com.accountabilibuddy.shared.session.PhoneAway
import com.accountabilibuddy.shared.session.PhoneAwayKind
import com.accountabilibuddy.shared.session.PhoneEndSettings
import com.accountabilibuddy.shared.session.PhoneLockArea
import com.accountabilibuddy.shared.session.PhoneSessionCodec
import com.accountabilibuddy.shared.session.PhoneSessionLocks
import com.accountabilibuddy.shared.session.PhoneSessionMode
import com.accountabilibuddy.shared.session.PhoneSessionProfile
import com.accountabilibuddy.shared.session.PhoneSessionProfileJson
import com.accountabilibuddy.shared.session.PhoneSessionProfileUse
import com.accountabilibuddy.shared.session.PhoneSessionRecord
import com.accountabilibuddy.shared.session.PhoneSessionStoreData
import com.accountabilibuddy.shared.session.PhoneSessionUsageRules
import com.accountabilibuddy.shared.session.PhoneSessionUsageSummary
import com.accountabilibuddy.shared.session.SessionEndCostRules
import java.time.Duration
import java.time.Instant
import java.time.ZoneId

/**
 * PS2c (ADR 85, plan check B1/B9, PS6a review S4/S8): the phone's lock seam and enforcement. The pure rules run on tables; the app files that cannot run on the JVM are
 * pinned by source scans, among them the written classification of every lock consumer (B9).
 */
object Ps2cSelfCheck {
    private val AB = PhoneSessionMode.ACCOUNTABILI_BUDDY
    private val PB = PhoneSessionMode.PRODUCTA_BUDDY
    private val t0 = Instant.parse("2026-11-16T17:00:00Z")
    private fun at(minutes: Long): Instant = t0.plusSeconds(minutes * 60)
    private val everything: Set<PhoneLockArea> = PhoneLockArea.values().toSet()
    private val nothingOn = PhoneSessionProfileUse.NOTHING
    private val lmpOn = PhoneSessionProfileUse(lockMyPhoneOrEssentials = true, curbox = false)
    private val curboxOn = PhoneSessionProfileUse(lockMyPhoneOrEssentials = false, curbox = true)
    private val settingsAreas = setOf(
        PhoneLockArea.PLAN, PhoneLockArea.SYNC, PhoneLockArea.REMOVAL, PhoneLockArea.RESTORE, PhoneLockArea.SETUP_REVOKES, PhoneLockArea.OTHER_SETTINGS, PhoneLockArea.GUARDS_OTHER,
    )

    private fun record(id: String = "s-1", endsAt: Instant? = at(50), isTest: Boolean = false, ended: Instant? = null, away: List<PhoneAway> = emptyList(), profileId: String = "p-1") =
        PhoneSessionRecord(id, profileId, "Deep work", at(0), at(0), endsAt, isTest = isTest, away = away, endedAt = ended)

    fun run(report: CheckReport) {
        scopes(report)
        seam(report)
        gates(report)
        endCost(report)
        profiles(report)
        blocks(report)
        usage(report)
        controllerUsage(report)
        sources(report)
    }

    // --- R11 by mode, and what is locked when sessions overlap ------------------------------------------------------------------

    private fun scopes(report: CheckReport) {
        val running = record()
        report.check("ps2c/scope (R11): an AccountabiliBuddy phone session locks every area, whatever its profile", PhoneSessionLocks.scope(AB, running, nothingOn) == everything && PhoneSessionLocks.scope(AB, running, lmpOn) == everything)
        report.check("ps2c/scope (R11): a ProductaBuddy plain timer locks only the session profiles and the end-early setting",
            PhoneSessionLocks.scope(PB, running, nothingOn) == setOf(PhoneLockArea.SESSION_PROFILES, PhoneLockArea.END_EARLY_SETTING))
        report.check("ps2c/scope (R11): a ProductaBuddy session whose profile names LockMyPhone also locks the LockMyPhone and Essentials rows",
            PhoneSessionLocks.scope(PB, running, lmpOn) == setOf(PhoneLockArea.SESSION_PROFILES, PhoneLockArea.END_EARLY_SETTING, PhoneLockArea.GUARDS_LMP_ESSENTIALS))
        report.check("ps2c/scope (R11): ... Curbox, the Curbox rows",
            PhoneSessionLocks.scope(PB, running, curboxOn) == setOf(PhoneLockArea.SESSION_PROFILES, PhoneLockArea.END_EARLY_SETTING, PhoneLockArea.CURBOX_ROWS))
        report.check("ps2c/scope (owner answer): a ProductaBuddy phone session leaves Plan, sync, removal, restore and the other Settings open, whatever its profile",
            listOf(nothingOn, lmpOn, curboxOn, PhoneSessionProfileUse(true, true)).all { use -> PhoneSessionLocks.scope(PB, running, use).none { it in settingsAreas } })
        report.check("ps2c/scope: no session, a test session and an ended one lock nothing",
            PhoneSessionLocks.scope(AB, null, nothingOn).isEmpty() && PhoneSessionLocks.scope(AB, record(isTest = true), nothingOn).isEmpty() && PhoneSessionLocks.scope(AB, record(ended = at(10)), nothingOn).isEmpty())
        // B1: the scope has no clock to compare with: a set end long past still locks (only the controller's trusted tick ends a session).
        report.check("ps2c/b1: a session whose set end is far in the past still locks (the phone's clock is never compared)", PhoneSessionLocks.scope(AB, record(endsAt = t0.minus(Duration.ofDays(2))), nothingOn) == everything)
        report.check("ps2c/scope: a profile turns on LockMyPhone through its LockMyPhone profile and Curbox through a focus group or through blocks only",
            OwnSessionEnforcement.Profile("Study", false, null).use == lmpOn && OwnSessionEnforcement.Profile(null, false, "Group").use == curboxOn && OwnSessionEnforcement.Profile(null, false, null, curboxBlocks = true).use == curboxOn &&
                OwnSessionEnforcement.Profile.NONE.use == nothingOn)
    }

    private fun seam(report: CheckReport) {
        val abOwn = PhoneSessionLocks.scope(AB, record(), nothingOn)
        val pbOwn = PhoneSessionLocks.scope(PB, record(), nothingOn)
        val pbLmp = PhoneSessionLocks.scope(PB, record(), lmpOn)
        report.check("ps2c/seam: a PC (or test) session locks every area in both modes (ADR 68 keeps its no-escape-hatch locks)",
            everything.all { PhoneSessionLocks.locks(it, otherSessionRunning = true, own = emptySet()) } && PhoneSessionLocks.locked(true, pbOwn) == everything)
        report.check("ps2c/seam: only the own session running: its scope", PhoneSessionLocks.locked(false, pbOwn) == pbOwn && PhoneSessionLocks.locked(false, abOwn) == everything)
        report.check("ps2c/seam: nothing runs, nothing is locked", PhoneSessionLocks.locked(false, emptySet()).isEmpty() && !PhoneSessionLocks.locks(PhoneLockArea.REMOVAL, false, emptySet()))
        report.check("ps2c/seam (PB own): Plan, removal and sync are open while its profile's enforcement rows are locked",
            !PhoneSessionLocks.locks(PhoneLockArea.PLAN, false, pbLmp) && !PhoneSessionLocks.locks(PhoneLockArea.REMOVAL, false, pbLmp) && !PhoneSessionLocks.locks(PhoneLockArea.SYNC, false, pbLmp) &&
                PhoneSessionLocks.locks(PhoneLockArea.GUARDS_LMP_ESSENTIALS, false, pbLmp) && !PhoneSessionLocks.locks(PhoneLockArea.CURBOX_ROWS, false, pbLmp))
        report.check("ps2c/seam (offline double start): a PC session and a ProductaBuddy own timer together lock everything (the stricter)", PhoneSessionLocks.locked(true, pbOwn) == everything)
        report.check("ps2c/seam: AccountabiliBuddy own locks removal and sync (the two areas the removal lock reads) and the plan", listOf(PhoneLockArea.REMOVAL, PhoneLockArea.SYNC, PhoneLockArea.PLAN).all { PhoneSessionLocks.locks(it, false, abOwn) })
        report.check("ps2c/seam: the lock's end is the later of two, and no set end is the latest",
            PhoneSessionLocks.laterEnd(at(10), at(20)) == at(20) && PhoneSessionLocks.laterEnd(at(20), at(10)) == at(20) && PhoneSessionLocks.laterEnd(at(10), null) == null && PhoneSessionLocks.laterEnd(null, at(10)) == null && PhoneSessionLocks.laterEnd(null, null) == null)
        report.check("ps2c/seam: the restore area is locked by an AccountabiliBuddy phone session, open in ProductaBuddy", PhoneSessionLocks.locks(PhoneLockArea.RESTORE, false, abOwn) && !PhoneSessionLocks.locks(PhoneLockArea.RESTORE, false, pbOwn))
    }

    // --- S4 / S8: the mode switch and the pause see the own session -------------------------------------------------------------

    private fun gates(report: CheckReport) {
        val pending = GuidanceState(GuidanceMode.Guarding, null, GuidancePending(at(0), at(10)))
        val running = PhoneSessionLocks.anyRunning(otherSessionRunning = false, ownRunning = true)
        report.check("ps2c/gate (S8): any session includes the phone's own, in either mode", running && PhoneSessionLocks.anyRunning(true, false) && PhoneSessionLocks.anyRunning(true, true) && !PhoneSessionLocks.anyRunning(false, false))
        val during = GuidanceRules.applyIfDue(pending, at(30), sessionActive = running)
        report.check("ps2c/gate (S8): a pending ProductaBuddy does not apply while a phone session runs, even long after its wait", during.mode == GuidanceMode.Guarding && during.pending != null)
        val after = GuidanceRules.applyIfDue(pending, at(30), sessionActive = PhoneSessionLocks.anyRunning(false, false))
        report.check("ps2c/gate (S8): ... and applies once no session runs", after.mode == GuidanceMode.Companion && after.pending == null)
        report.check("ps2c/gate (S8): a switch asked during the session is refused", GuidanceRules.requestCompanion(GuidanceState(GuidanceMode.Guarding, null, null), at(0), Duration.ofMinutes(10), sessionActive = running) == null)
        report.check("ps2c/gate (S4): the paid 'now' still applies with the session running (ADR 68's own path)",
            GuidanceRules.applyIfDue(GuidanceState(GuidanceMode.Guarding, null, GuidancePending(at(0), at(10), duringSession = true)), at(30), running).mode == GuidanceMode.Companion)
        val pauseAsked = PauseState(requestedAt = at(0), waitMinutes = 10)
        report.check("ps2c/gate (S4): the pause master switch's request waits while a phone session runs, then applies after it",
            PauseRules.advance(pauseAsked, at(30), ZoneId.of("UTC"), sessionRunning = running).pausedAt == null && PauseRules.advance(pauseAsked, at(30), ZoneId.of("UTC"), sessionRunning = false).pausedAt != null)
    }

    // --- S4: what ending costs, with the phone's own session --------------------------------------------------------------------

    private fun endCost(report: CheckReport) {
        val timed = record()
        val settings = PhoneEndSettings(waitSeconds = 90, minReasonLength = 20)
        val own = SessionEndCostRules.own(AB, timed, settings)
        report.check("ps2c/cost (S4): an AccountabiliBuddy own session with a set end is NOT free", !own.free && own.hasSetEnd)
        report.check("ps2c/cost (S4): it costs the phone's own end-early wait and reason", own.waitSeconds == 90 && own.minReasonLength == 20)
        report.check("ps2c/cost: the defaults are the PC's 60 seconds and 15 characters", SessionEndCostRules.own(AB, timed, PhoneEndSettings.DEFAULT).let { it.waitSeconds == 60 && it.minReasonLength == 15 })
        report.check("ps2c/cost: ProductaBuddy ends at once, so it is free", SessionEndCostRules.own(PB, timed, settings).free)
        val open = SessionEndCostRules.own(AB, record(endsAt = null), settings)
        report.check("ps2c/cost: a session with no set end ends on command: free, and 'after this session ends' means by hand", open.free && !open.hasSetEnd)
        report.check("ps2c/cost: a test session ends on command", SessionEndCostRules.own(AB, record(isTest = true), settings).free)
        val pc = EndCostTerms(waitSeconds = 120, minReasonLength = 30, free = false, hasSetEnd = true)
        val stricter = SessionEndCostRules.stricter(own, pc)!!
        report.check("ps2c/cost (offline double start): both sessions cost: the longer wait and the longer reason", !stricter.free && stricter.waitSeconds == 120 && stricter.minReasonLength == 30)
        val pcFree = EndCostTerms(60, 15, free = true, hasSetEnd = true)
        val mixed = SessionEndCostRules.stricter(own, pcFree)!!
        report.check("ps2c/cost: a free PC session does not make the phone's own cost free, and its numbers do not count", !mixed.free && mixed.waitSeconds == 90 && mixed.minReasonLength == 20)
        report.check("ps2c/cost: both free is free", SessionEndCostRules.stricter(open, pcFree)!!.free)
        report.check("ps2c/cost: no set end on either side means by hand", !SessionEndCostRules.stricter(own, EndCostTerms(60, 15, free = false, hasSetEnd = false))!!.hasSetEnd)
        report.check("ps2c/cost: one side missing is the other side, none is none", SessionEndCostRules.stricter(own, null) == own && SessionEndCostRules.stricter(null, pc) == pc && SessionEndCostRules.stricter(null, null) == null)
    }

    // --- The profile ------------------------------------------------------------------------------------------------------------

    private fun profiles(report: CheckReport) {
        val full = PhoneSessionProfile("p-1", "Study", lmpProfile = "Study lock", curboxFocusGroup = "g1", curboxBlocks = listOf(CurboxBlockRef("app", "b1"), CurboxBlockRef("reel", "reelBlocker")),
            distractingApps = listOf("com.social", "com.video"), defaultMinutes = 25, noSetEnd = true, editedAt = 123L)
        val plain = PhoneSessionProfile("p-2", "Timer")
        val text = PhoneSessionProfileJson.write(listOf(full, plain))
        report.check("ps2c/profile: the profiles round-trip through their text", PhoneSessionProfileJson.read(text) == listOf(full, plain))
        report.check("ps2c/profile: text that is not the format reads as unreadable, never as 'no profiles'", PhoneSessionProfileJson.read("{not json") == null && PhoneSessionProfileJson.read("{}") == null)
        report.check("ps2c/profile: a plain timer turns nothing on", plain.use == nothingOn && plain.enforcement == OwnSessionEnforcement.Profile.NONE)
        report.check("ps2c/profile: LockMyPhone profile, focus group and blocks are what it turns on",
            full.enforcement == OwnSessionEnforcement.Profile("Study lock", essentialsOnUnlock = false, curboxFocusGroup = "g1", curboxBlocks = true) && full.use == PhoneSessionProfileUse(true, true))
        report.check("ps2c/profile: blocks alone turn Curbox on for the lock scope", PhoneSessionProfile("p-3", "B", curboxBlocks = listOf(CurboxBlockRef("app", "x"))).use == curboxOn)
        report.check("ps2c/profile: the profile's own LockMyPhone profile and group are what the own session asks for (ProductaBuddy too)",
            OwnSessionEnforcement.lmpProfile(PB, full.enforcement, "Global", otherRunning = false) == "Study lock" && OwnSessionEnforcement.curboxGroup(PB, full.enforcement, "GlobalGroup", otherRunning = false) == "g1")
    }

    // --- Curbox blocks of the profile -------------------------------------------------------------------------------------------

    private fun blocks(report: CheckReport) {
        val a = CurboxBlockRef("app", "a")
        val b = CurboxBlockRef("keyword", "b")
        val c = CurboxBlockRef("reel", "c")
        val wanted = listOf(a, b, c)
        val start = CurboxSessionBlocksRule.step(true, wanted, mapOf(a to false, b to true, c to false), Memory())
        report.check("ps2c/blocks: at the start a wanted block that is off is turned on", start.actions.toSet() == setOf(Action.TurnOn(a), Action.TurnOn(c)))
        report.check("ps2c/blocks: only the ones that were off are ours; the one already on is remembered as on before", start.memory == Memory(ours = setOf(a, c), onBefore = setOf(b)))
        val again = CurboxSessionBlocksRule.step(true, wanted, mapOf(a to true, b to true, c to true), start.memory)
        report.check("ps2c/blocks: the next refresh with everything on does nothing and keeps the memory", again.actions.isEmpty() && again.memory == start.memory)
        val end = CurboxSessionBlocksRule.step(false, wanted, mapOf(a to true, b to true, c to true), start.memory)
        report.check("ps2c/blocks: at the end only the ones this session turned on are turned off; the one that was on before stays on", end.actions.toSet() == setOf(Action.TurnOff(a), Action.TurnOff(c)) && end.memory.isEmpty)
        report.check("ps2c/blocks: a block we turned on that was turned off in Curbox since is not turned off again", CurboxSessionBlocksRule.step(false, wanted, mapOf(a to false, b to true, c to true), start.memory).actions == listOf(Action.TurnOff(c)))
        val userOff = CurboxSessionBlocksRule.step(true, wanted, mapOf(a to false, b to true, c to true), start.memory)
        report.check("ps2c/blocks (S9): one the user turned off in Curbox during the session is turned on again", userOff.actions == listOf(Action.TurnOn(a)))
        val preOff = CurboxSessionBlocksRule.step(true, wanted, mapOf(a to true, b to false, c to true), start.memory)
        report.check("ps2c/blocks (S9): one that was on before the session and was switched off meanwhile is turned on again but stays not ours", preOff.actions == listOf(Action.TurnOn(b)) && b !in preOff.memory.ours && b in preOff.memory.onBefore)
        report.check("ps2c/blocks: Curbox not answering changes nothing and keeps the memory", CurboxSessionBlocksRule.step(true, wanted, null, start.memory).let { it.actions.isEmpty() && it.memory == start.memory } && CurboxSessionBlocksRule.step(false, wanted, null, start.memory).memory == start.memory)
        report.check("ps2c/blocks: nothing wanted and nothing remembered asks Curbox for nothing", CurboxSessionBlocksRule.step(true, emptyList(), mapOf(a to false), Memory()).actions.isEmpty() && CurboxSessionBlocksRule.step(false, wanted, mapOf(a to false), Memory()).actions.isEmpty())
        report.check("ps2c/blocks: a block that vanished from Curbox is forgotten", CurboxSessionBlocksRule.step(true, wanted, mapOf(b to true), start.memory).memory.ours.isEmpty())
        val failedOn = CurboxSessionBlocksRule.settle(start, listOf(Action.TurnOn(a)))
        report.check("ps2c/blocks: a block Curbox refused to turn on is not remembered as ours", a !in failedOn.ours && c in failedOn.ours)
        val failedOff = CurboxSessionBlocksRule.settle(end, listOf(Action.TurnOff(a)))
        report.check("ps2c/blocks: a block Curbox refused to turn off stays ours, to be tried again", failedOff.ours == setOf(a))
        report.check("ps2c/blocks: after emergency time (everything of ours off again) the next look turns them on again",
            CurboxSessionBlocksRule.step(true, wanted, mapOf(a to false, b to true, c to false), end.memory).actions.toSet().containsAll(setOf(Action.TurnOn(a), Action.TurnOn(c))))
    }

    // --- Pickups and distraction ------------------------------------------------------------------------------------------------

    private fun usage(report: CheckReport) {
        val away = listOf(PhoneAway(PhoneAwayKind.STEP_AWAY, at(20), at(30)), PhoneAway(PhoneAwayKind.PAUSED, at(40), null))
        val rec = record(endsAt = at(60), away = away)
        val spans = PhoneSessionUsageRules.focusSpans(rec, at(50))
        report.check("ps2c/usage: focus is the session less every stretch away, an open one included", spans.map { it.from to it.to } == listOf(at(0) to at(20), at(30) to at(40)))
        val unlocks = listOf(at(5), at(25), at(35), at(45), at(10))
        report.check("ps2c/usage: only unlocks inside focus are pickups (an unlock while stepped away or paused is not)", PhoneSessionUsageRules.pickupsIn(spans, unlocks) == listOf(at(5), at(10), at(35)))
        val longest = PhoneSessionUsageRules.longestStretch(spans, PhoneSessionUsageRules.pickupsIn(spans, unlocks))
        report.check("ps2c/usage: the longest stretch down is the longest gap inside a focus stretch", longest == Duration.ofMinutes(10), longest.toString())
        report.check("ps2c/usage: with no unlock the longest stretch is the longest focus stretch whole", PhoneSessionUsageRules.longestStretch(spans, emptyList()) == Duration.ofMinutes(20))
        val summary = PhoneSessionUsageRules.summarize(rec, at(50), unlocks, distractionSeconds = 4 * 60 + 59)
        report.check("ps2c/usage: the summary: pickups, longest stretch in seconds, distraction in whole minutes", summary == PhoneSessionUsageSummary(3, 600L, 4), summary.toString())
        val noAccess = PhoneSessionUsageRules.summarize(rec, at(50), null, null)
        report.check("ps2c/usage: without Usage access every number is null (a dash), never a zero", noAccess == PhoneSessionUsageSummary(null, null, null))
        report.check("ps2c/usage: no distracting app named is null, not a zero", PhoneSessionUsageRules.summarize(rec, at(50), unlocks, null).distractionMinutes == null)
        report.check("ps2c/usage: a quiet session has zero pickups and a measured zero", PhoneSessionUsageRules.summarize(rec, at(50), emptyList(), 0L).let { it.pickups == 0 && it.distractionMinutes == 0 })
        val ended = rec.copy(endedAt = at(50), usage = summary)
        val round = PhoneSessionCodec.read(PhoneSessionCodec.write(PhoneSessionStoreData(log = listOf(ended, record(id = "s-2", ended = at(5)))))) ?: error("unreadable")
        report.check("ps2c/usage: the summary round-trips with the sealed log, and an older record has none", round.log.first().usage == summary && round.log.last().usage == null)
        report.check("ps2c/usage: a null part stays null through the log", PhoneSessionCodec.read(PhoneSessionCodec.write(PhoneSessionStoreData(log = listOf(ended.copy(usage = noAccess)))))!!.log.single().usage == noAccess)
    }

    private fun controllerUsage(report: CheckReport) {
        val world = Ps2aSelfCheck.World()
        val started = world.controller.start("p-1", "Deep work", null) as PhoneSessionStart.Started
        val id = started.record.id
        report.check("ps2c/controller: usage can't be recorded for a session that is still running (it is not in the log)", !world.controller.recordUsage(id, PhoneSessionUsageSummary(1, 60, 1)))
        world.controller.endOnCommand()
        val first = PhoneSessionUsageSummary(2, 120L, 3)
        report.check("ps2c/controller: the summary joins the ended session's log entry", world.controller.recordUsage(id, first) && world.controller.log.single().usage == first)
        report.check("ps2c/controller: it is set once (a second call changes nothing)", !world.controller.recordUsage(id, PhoneSessionUsageSummary(9, 9, 9)) && world.controller.log.single().usage == first)
        report.check("ps2c/controller: an id that is not in the log is refused", !world.controller.recordUsage("nope", first))
        val reopened = world.restart()
        report.check("ps2c/controller: it is saved: a restart still has it", reopened.log.single().usage == first)
    }

    // --- Source scans: the lock seam's written classification (B9), the wiring, trusted time ------------------------------------

    private fun sources(report: CheckReport) {
        fun code(path: String) = Sync1cCheck.appSource(path)?.replace("\r\n", "\n")?.let(Sync1cCheck::code)
        val placement = code("placement/PlacementSession.kt")
        val guards = code("placement/GuardsLock.kt")
        val app = code("CompanionApp.kt")
        val cost = code("guidance/SessionEndCost.kt")
        val launch = code("essentials/EssentialsLaunch.kt")
        val curbox = code("curbox/CurboxHardMode.kt")
        val blocks = code("curbox/CurboxSessionBlocks.kt")
        val runtime = code("session/PhoneSessionRuntime.kt")
        val profiles = code("session/PhoneSessionProfiles.kt")
        val vm = code("ui/CompanionViewModel.kt")
        val controls = code("ui/GuardControls.kt")
        report.check("ps2c/sources: the sources were found", listOf(placement, guards, app, cost, launch, curbox, blocks, runtime, profiles, vm, controls).all { it != null })

        // B9, the classification: every consumer of the lock seam, each with how many calls of which door. Anything new fails here until it is classified.
        val table = listOf(
            Row("CompanionApp.kt", locking = 3, anyRunning = 1, guardsAt = 2, why = "calendar overlay -> OTHER_SETTINGS; removal lock -> REMOVAL + SYNC (2 calls); pause/guidance gate -> anyRunning (S4/S8); deep work environment + lunchSessionRunning -> GuardsLock.at (OTHER_SETTINGS)"),
            Row("goals/PhoneGoalReview.kt", guardsAt = 1, why = "goal review hidden in a session -> GuardsLock.at (OTHER_SETTINGS)"),
            Row("tasks/TasksUiPrefs.kt", guardsAt = 1, why = "task shelf waiting -> GuardsLock.at (OTHER_SETTINGS)"),
            Row("ui/CalendarRulesSection.kt", guardsAt = 2, why = "calendar rules -> GuardsLock.at (OTHER_SETTINGS)"),
            Row("ui/CompanionViewModel.kt", guardsAt = 3, why = "the guards lock flow (OTHER_SETTINGS) and the plan sentence (PLAN) -> GuardsLock.at; areas flow -> areasAt"),
            Row("ui/settings/TasksSettingsPage.kt", guardsAt = 1, why = "task list rebuild -> GuardsLock.at (OTHER_SETTINGS)"),
            Row("placement/GuardsLock.kt", locking = 1, why = "the door itself: locking(area)"),
            Row("placement/PlacementGuard.kt", locking = 1, why = "the phone guard switch -> GUARDS_OTHER"),
            Row("sync/SleepSettingsSync.kt", locking = 1, why = "sleep switches -> OTHER_SETTINGS"),
            Row("ui/plan/EventActions.kt", locking = 1, why = "plan event changes -> PLAN"),
            // Placement-only: never the phone's own session (placement is the PC's feature).
            Row("notify/ParkedNotice.kt", current = 1, why = "placement-only: the parked notice"),
            Row("placement/PhoneDownActivity.kt", current = 1, why = "placement-only: Phone down"),
            Row("placement/PlacementDetector.kt", current = 1, why = "placement-only: the sensors"),
            Row("placement/WorkUseChooser.kt", current = 1, why = "placement-only: work use chooser"),
            Row("ui/TodayPane.kt", current = 1, why = "placement-only: the work use card's visibility"),
            Row("ui/WorkUseCard.kt", current = 2, why = "placement-only: the work use card"),
        )
        val dir = listOf(java.io.File("app/src/main/java/com/accountabilibuddy/companion"), java.io.File("../app/src/main/java/com/accountabilibuddy/companion")).firstOrNull { it.isDirectory }
        val door = Regex("""PlacementSession\.(current|locking|anyRunning)\(|\blockSeam\.locking\(|GuardsLock\.at\(""")
        val found = mutableMapOf<String, Row>()
        dir?.walkTopDown()?.filter { it.isFile && it.extension == "kt" }?.forEach { file ->
            val text = Sync1cCheck.code(file.readText().replace("\r\n", "\n"))
            val hits = door.findAll(text).map { it.value }.toList()
            if (hits.isNotEmpty()) {
                found[file.relativeTo(dir).path.replace('\\', '/')] = Row(
                    "",
                    current = hits.count { it.startsWith("PlacementSession.current") },
                    locking = hits.count { it.startsWith("PlacementSession.locking") || it.startsWith("lockSeam.locking") },
                    anyRunning = hits.count { it.startsWith("PlacementSession.anyRunning") },
                    guardsAt = hits.count { it.startsWith("GuardsLock.at") },
                )
            }
        }
        val wanted = table.associateBy { it.file }
        val same = found.keys == wanted.keys && found.all { (file, row) -> wanted[file]?.counts() == row.counts() }
        report.check("ps2c/sources (B9): every consumer of the lock seam is in the written classification, with the doors it calls (PlacementSession.current / locking / anyRunning, GuardsLock.at)", same,
            "found " + found.map { "${it.key}=${it.value.counts()}" }.sorted() + " wanted " + table.map { "${it.file}=${it.counts()}" }.sorted())
        val placementOnly = table.filter { it.current > 0 }.map { it.file }
        report.check("ps2c/sources (B9): the placement-only consumers read PlacementSession.current and no lock door", placementOnly.all { file -> found[file]?.let { f -> f.locking == 0 && f.anyRunning == 0 && f.guardsAt == 0 } == true })
        report.check("ps2c/sources (B9): no lock consumer reads PlacementSession.current any more", found.filter { it.key !in placementOnly }.all { it.value.current == 0 })

        // B1: the own session is read from the store's flag; the caller's `now` reaches nothing that can release a lock.
        val ownRecord = placement?.let { Sync1cCheck.body(it, "fun ownRecord(", "\n") }
        val ownAreas = placement?.let { Sync1cCheck.body(it, "fun ownAreas(", "\n    }\n") }
        report.check("ps2c/sources (B1): the own session is the store's running flag: no clock in how it is found or what it locks",
            ownRecord != null && ownRecord.contains("app.phoneSession.running.value?.takeIf { it.isRunning && !it.isTest }") && ownAreas != null && !ownAreas.contains("now") && !ownAreas.contains("Instant"))
        report.check("ps2c/sources (B1): the placement door never returns the own session, the lock door asks the pure seam", placement != null &&
            Sync1cCheck.body(placement, "fun current(", "\n")!!.let { !it.contains("own") } && placement.contains("PhoneSessionLocks.locks(area, other != null, ownAreas(app))") &&
            placement.contains("PhoneSessionLocks.locked(otherLocking(app, now) != null, ownAreas(app))") && placement.contains("PhoneSessionLocks.anyRunning(otherLocking(app, now) != null, ownRecord(app) != null)"))
        report.check("ps2c/sources (B1): the own part of the seam reads no wall clock", placement != null && !placement.contains("Instant.now()") && !placement.contains("System.currentTimeMillis"))
        report.check("ps2c/sources (R11): the lock door's PC/test part ignores ADR 60's 10-minute release (the locks hold while a PC is off)", placement != null && placement.contains("fromPc(app, now, ignoreOff = true) ?: fromTest(app, now)"))
        report.check("ps2c/sources (R11): the guards lock takes the scope by mode through the door, with the Settings pages as its default area",
            guards != null && guards.contains("area: PhoneLockArea = PhoneLockArea.OTHER_SETTINGS") && guards.contains("PlacementSession.locking(app, now, area)") && guards.contains("PlacementSession.lockedAreas(app, now)"))
        report.check("ps2c/sources (B9): the removal lock reads the removal AND the sync area",
            app != null && app.contains("lockSeam.locking(this, trusted, com.accountabilibuddy.shared.session.PhoneLockArea.REMOVAL) != null ||") && app.contains("lockSeam.locking(this, trusted, com.accountabilibuddy.shared.session.PhoneLockArea.SYNC) != null"))
        report.check("ps2c/sources (B9): the plan lock names the plan area, the phone guard switch the guards area",
            vm != null && vm.contains("com.accountabilibuddy.shared.session.PhoneLockArea.PLAN)?.planSentence()") && code("placement/PlacementGuard.kt")?.contains("PhoneLockArea.GUARDS_OTHER") == true && code("ui/plan/EventActions.kt")?.contains("PhoneLockArea.PLAN") == true)

        // S4/S8: the gates.
        report.check("ps2c/sources (S8): the pause master switch and the mode switch (guidance) both wait on any session, the own one included",
            app != null && app.contains("pause.sessionRunning = { now -> com.accountabilibuddy.companion.placement.PlacementSession.anyRunning(this, now) }") && app.contains("guidance.sessionRunning = pause.sessionRunning"))
        report.check("ps2c/sources (S4): the end cost reads the own session through the pure rule and merges it with a PC session's",
            cost != null && cost.contains("SessionEndCostRules.own(EssentialsLaunch.mode(app), it, app.phoneSession.endSettings())") && cost.contains("SessionEndCostRules.stricter(own, other)") && !cost.contains("val isPcSession = EssentialsLaunch.realSessionStart(app, now) != null"))
        report.check("ps2c/sources (S4): the end cost's own part reads the phone's real session only (a test session stays free)", cost != null && cost.contains("PlacementSession.ownRecord(app)"))

        // Areas in the Compose layer: Essentials and Curbox sections lock by area, the rest keep the old scope.
        report.check("ps2c/sources (R11): the LockMyPhone/Essentials and Curbox sections lock by their area; the scope also reads the area locks the view model hands down",
            controls != null && controls.contains("area != null && area in LocalGuardsAreas.current") && code("ui/EssentialsSection.kt")?.contains("GuardsScope(com.accountabilibuddy.shared.session.PhoneLockArea.GUARDS_LMP_ESSENTIALS)") == true &&
                code("ui/settings/CurboxSection.kt")?.contains("GuardsScope(com.accountabilibuddy.shared.session.PhoneLockArea.CURBOX_ROWS)") == true && code("ui/CurboxSetupPage.kt")?.contains("GuardsScope(com.accountabilibuddy.shared.session.PhoneLockArea.CURBOX_ROWS)") == true &&
                code("ui/MainScreen.kt")?.contains("LocalGuardsAreas provides guardsAreas") == true)
        report.check("ps2c/sources: the view model re-reads the lock when the phone session changes, on trusted time",
            vm != null && vm.contains("getApplication<CompanionApp>().phoneSession.running") && !vm.substringAfter("val guardsLock").substringBefore("fun openRemoveDevice").contains("Instant.now()"))

        // Enforcement: profile, Curbox.
        report.check("ps2c/sources (N6): the essentials hook reads the profile the session started with (the record's profile id) through the profiles store, and nothing when none is found",
            launch != null && launch.contains("?.let(app.phoneSessionProfiles::enforcementOf) ?: OwnSessionEnforcement.Profile.NONE") && profiles != null && profiles.contains("byId(record.profileId)?.enforcement ?: OwnSessionEnforcement.Profile.NONE"))
        report.check("ps2c/sources (R11): a profile can't be changed while a real phone session runs", profiles != null && profiles.contains("if (app.phoneSession.realSessionRunning()) return false"))
        report.check("ps2c/sources: Curbox hard mode asks for the profile's blocks first, in its own guarded call", curbox != null && curbox.contains("runCatching { CurboxSessionBlocks.check(context, now) }"))
        report.check("ps2c/sources: the blocks follow the own session and emergency time, ask Curbox only when something is wanted or remembered, and use the pure rule",
            blocks != null && blocks.contains("val record = PlacementSession.ownRecord(app)") && blocks.contains("record != null && !app.emergency.isActive(now)") && blocks.contains("if (memory.isEmpty && (!running || wanted.isEmpty())) return") &&
                blocks.contains("CurboxSessionBlocksRule.step(running, wanted, current, memory)") && blocks.contains("CurboxSessionBlocksRule.settle(step, failed)") && blocks.contains("client.set(action.block.kind, action.block.id, action is CurboxSessionBlocksRule.Action.TurnOn)"))
        report.check("ps2c/sources: no direct LockMyPhone start or stop path was added (the hold and START_LOCK go through the essentials hook)",
            listOfNotNull(blocks, runtime, profiles, code("session/PhoneSessionUsage.kt")).none { it.contains("sendSessionLock") || it.contains("LMP_STOP") || it.contains("startSessionLock") })

        // Usage (R14, R12, N4).
        val usage = code("session/PhoneSessionUsage.kt")
        report.check("ps2c/sources (N4): the usage is read off the controller's lock and saved set-once; a test session never gets one",
            runtime != null && runtime.contains("if (!record.isTest) recordUsageLater(record)") && runtime.contains("scope.launch { runCatching { controller.recordUsage(record.id, PhoneSessionUsage.read(app, record, end)) } }"))
        report.check("ps2c/sources (R14): the summary stays on the phone: the sync writer, the file's pieces and the own-device file never mention it",
            listOf("session/PhoneSessionSyncWriter.kt", "session/PhoneSessionFiles.kt").all { path -> (code(path) ?: sharedSource(path))?.let { !it.contains("usage", ignoreCase = true) } == true } && (sharedSource("sync/SessionPieces.kt")?.let { !it.contains("usage", ignoreCase = true) } == true))
        report.check("ps2c/sources (R12): the new session code reads no wall clock", listOfNotNull(usage, profiles).all { !it.contains("Instant.now()") && !it.contains("System.currentTimeMillis") && !it.contains("LocalDateTime.now") })
    }

    private data class Row(val file: String, val current: Int = 0, val locking: Int = 0, val anyRunning: Int = 0, val guardsAt: Int = 0, val why: String = "") {
        fun counts() = "$current/$locking/$anyRunning/$guardsAt"
    }

    private fun sharedSource(path: String): String? =
        listOf(java.io.File("shared/src/main/java/com/accountabilibuddy/shared/$path"), java.io.File("../shared/src/main/java/com/accountabilibuddy/shared/$path"))
            .firstOrNull { it.isFile }?.readText()?.replace("\r\n", "\n")?.let(Sync1cCheck::code)
}
