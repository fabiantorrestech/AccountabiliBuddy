package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.session.NightHandoverEffect
import com.accountabilibuddy.shared.session.NightHandoverRule
import com.accountabilibuddy.shared.session.NightHandoverState
import com.accountabilibuddy.shared.session.OwnSessionEnforcement
import com.accountabilibuddy.shared.session.OwnSessionEnforcement.Profile
import com.accountabilibuddy.shared.session.PhoneSessionMode
import java.time.Duration
import java.time.Instant

/**
 * PS6a (ADR 85, plan check B7) and its fix round: the essentials hook for the phone's own session. The pure rules ([OwnSessionEnforcement],
 * [NightHandoverRule]) are run on tables; source scans pin that the app's essentials code uses them and that nothing in the lock decisions reads a wall clock.
 */
object Ps6aSelfCheck {
    private val AB = PhoneSessionMode.ACCOUNTABILI_BUDDY
    private val PB = PhoneSessionMode.PRODUCTA_BUDDY
    private val none = Profile.NONE
    private val withLmp = Profile(lmpProfile = "Study", essentialsOnUnlock = true, curboxFocusGroup = "Focus group")

    fun run(report: CheckReport) {
        order(report)
        pbDoesNotTurnOnGlobals(report)
        pbWithAnotherSession(report)
        abTurnsThemOn(report)
        onBreak(report)
        night(report)
        sources(report)
    }

    private fun order(report: CheckReport) {
        report.check("ps6a/order: own before PC before test", OwnSessionEnforcement.runningStart("own", "pc", "test") == "own")
        report.check("ps6a/order: PC before test when no own", OwnSessionEnforcement.runningStart(null, "pc", "test") == "pc")
        report.check("ps6a/order: test last", OwnSessionEnforcement.runningStart<String>(null, null, "test") == "test")
        report.check("ps6a/order: nothing running is null", OwnSessionEnforcement.runningStart<String>(null, null, null) == null)
        report.check("ps6a/order: own wins over a PC session started earlier (offline double start)", OwnSessionEnforcement.runningStart(2, 1, null) == 2)
    }

    private fun pbDoesNotTurnOnGlobals(report: CheckReport) {
        report.check("ps6a/pb: no global LockMyPhone profile for an own session", OwnSessionEnforcement.lmpProfile(PB, none, "Accountabilibuddy", otherRunning = false) == null)
        report.check("ps6a/pb: no global Essentials screen on unlock for an own session", !OwnSessionEnforcement.essentialsOnUnlock(PB, none, globalOnUnlock = true, otherRunning = false))
        report.check("ps6a/pb: no global Curbox group for an own session", OwnSessionEnforcement.curboxGroup(PB, none, "Hard group", otherRunning = false) == null)
        report.check("ps6a/pb: the profile's own LockMyPhone profile is used", OwnSessionEnforcement.lmpProfile(PB, withLmp, "Accountabilibuddy", otherRunning = false) == "Study")
        report.check("ps6a/pb: the profile's own Essentials switch is used", OwnSessionEnforcement.essentialsOnUnlock(PB, withLmp, globalOnUnlock = false, otherRunning = false))
        report.check("ps6a/pb: the profile's own Curbox group is used", OwnSessionEnforcement.curboxGroup(PB, withLmp, null, otherRunning = false) == "Focus group")
    }

    // Review B1: the offline double start. A phone timer (PB, plain) must never cancel a PC session's locks; the global ones stay.
    private fun pbWithAnotherSession(report: CheckReport) {
        report.check("ps6a/b1: PB own timer + a PC session: the global LockMyPhone profile stays", OwnSessionEnforcement.lmpProfile(PB, none, "Accountabilibuddy", otherRunning = true) == "Accountabilibuddy")
        report.check("ps6a/b1: PB own timer + a PC session: the global Essentials screen stays", OwnSessionEnforcement.essentialsOnUnlock(PB, none, globalOnUnlock = true, otherRunning = true))
        report.check("ps6a/b1: PB own timer + a PC session: the global Curbox group stays", OwnSessionEnforcement.curboxGroup(PB, none, "Hard group", otherRunning = true) == "Hard group")
        report.check("ps6a/b1: PB own timer + a PC session with the global switch off: still off (nothing invented)",
            !OwnSessionEnforcement.essentialsOnUnlock(PB, none, globalOnUnlock = false, otherRunning = true) && OwnSessionEnforcement.lmpProfile(PB, none, null, otherRunning = true) == null)
        report.check("ps6a/b1: a profile's own LockMyPhone profile still wins over the global one while a PC session runs", OwnSessionEnforcement.lmpProfile(PB, withLmp, "Accountabilibuddy", otherRunning = true) == "Study")
    }

    private fun abTurnsThemOn(report: CheckReport) {
        report.check("ps6a/ab: falls back to the global LockMyPhone profile", OwnSessionEnforcement.lmpProfile(AB, none, "Accountabilibuddy", otherRunning = false) == "Accountabilibuddy")
        report.check("ps6a/ab: the Essentials screen follows the global switch (on)", OwnSessionEnforcement.essentialsOnUnlock(AB, none, globalOnUnlock = true, otherRunning = false))
        report.check("ps6a/ab: the Essentials screen follows the global switch (off)", !OwnSessionEnforcement.essentialsOnUnlock(AB, none, globalOnUnlock = false, otherRunning = false))
        report.check("ps6a/ab: falls back to the global Curbox group", OwnSessionEnforcement.curboxGroup(AB, none, "Hard group", otherRunning = false) == "Hard group")
        report.check("ps6a/ab: Curbox hard mode off hands no group over (S1): none", OwnSessionEnforcement.curboxGroup(AB, none, null, otherRunning = false) == null)
        report.check("ps6a/ab: a profile's own LockMyPhone profile beats the global one", OwnSessionEnforcement.lmpProfile(AB, withLmp, "Accountabilibuddy", otherRunning = false) == "Study")
        report.check("ps6a/ab: no global profile means none", OwnSessionEnforcement.lmpProfile(AB, none, null, otherRunning = false) == null)
    }

    private fun onBreak(report: CheckReport) {
        report.check("ps6a/break: false during the own session even when a PC is on a break", !OwnSessionEnforcement.onBreak(ownRunning = true, pcOnBreak = true))
        report.check("ps6a/break: a PC session's break still reads true with no own session", OwnSessionEnforcement.onBreak(ownRunning = false, pcOnBreak = true))
        report.check("ps6a/break: no break, no own session reads false", !OwnSessionEnforcement.onBreak(ownRunning = false, pcOnBreak = false))
    }

    // B2, the owner's design (2026-11-15): a session on this phone takes over the night; reminder, 5-minute card, Snooze, Cancel per mode.
    private fun night(report: CheckReport) {
        val t0 = Instant.parse("2026-11-16T04:00:00Z") // the night starts
        fun at(minutes: Long) = t0.plus(Duration.ofMinutes(minutes))
        val n = "night-1"
        val idle = NightHandoverState()

        report.check("ps6a/night: a running session holds the night back (any session, a plain PB timer too)",
            NightHandoverRule.holds(idle, n, sessionRunning = true, now = t0) && NightHandoverRule.holds(idle, n, sessionRunning = true, now = at(300)))
        report.check("ps6a/night: no session and nothing pending: the night is not held", !NightHandoverRule.holds(idle, n, sessionRunning = false, now = t0))
        report.check("ps6a/night: outside the night nothing is held", !NightHandoverRule.holds(idle, null, sessionRunning = true, now = t0))

        // The reminder: the night starts while a session runs: shown right away, once.
        val first = NightHandoverRule.step(idle, n, sessionRunning = true, now = t0)
        report.check("ps6a/night: the reminder shows right away when the night would start during a session", first.effects == listOf(NightHandoverEffect.SHOW_REMINDER))
        val again = NightHandoverRule.step(first.state, n, sessionRunning = true, now = at(5))
        report.check("ps6a/night: the reminder is shown once per night", again.effects.isEmpty() && again.state.sawSession)

        // The session ends during the night: a 5-minute card, then the night.
        val ended = NightHandoverRule.step(again.state, n, sessionRunning = false, now = at(60))
        report.check("ps6a/night: a session that ends during the night starts a 5-minute card",
            ended.state.startAt == at(65) && NightHandoverEffect.SHOW_COUNTDOWN in ended.effects && NightHandoverRule.pendingStart(ended.state, n, false, at(60)) == at(65))
        report.check("ps6a/night: the night stays held for the 5 minutes", NightHandoverRule.holds(ended.state, n, false, at(64)))
        report.check("ps6a/night: the night starts at 5 minutes (not held at the start time)", !NightHandoverRule.holds(ended.state, n, false, at(65)))
        val started = NightHandoverRule.step(ended.state, n, sessionRunning = false, now = at(65))
        report.check("ps6a/night: at 5 minutes the night starts as usual (the lock is asked for again, once)",
            started.state.done && NightHandoverEffect.NIGHT_STARTS in started.effects && NightHandoverRule.step(started.state, n, false, at(70)).effects.isEmpty())
        report.check("ps6a/night: nothing is shown before the 5 minutes pass", NightHandoverRule.step(ended.state, n, false, at(64)).effects.isEmpty())
        report.check("ps6a/night: a clock moved back never starts the night early", NightHandoverRule.holds(ended.state, n, false, at(-600)))

        // Snooze: 5 minutes from now, both modes; only while the card shows.
        val snoozed = NightHandoverRule.snooze(AB, ended.state, n, false, at(63))
        report.check("ps6a/night: Snooze moves the start to 5 minutes from now", snoozed != null && snoozed.state.startAt == at(68) && snoozed.state.snoozes == 1)
        report.check("ps6a/night: Snooze with no card is nothing", NightHandoverRule.snooze(AB, idle, n, false, at(63)) == null && NightHandoverRule.snooze(AB, started.state, n, false, at(66)) == null)

        // Cancel: ProductaBuddy only (AccountabiliBuddy offers Snooze only, Q1198).
        report.check("ps6a/night: PB can cancel tonight, AB can not", NightHandoverRule.canCancel(PB) && !NightHandoverRule.canCancel(AB))
        val cancelled = NightHandoverRule.cancel(PB, ended.state, n, false, at(61))
        report.check("ps6a/night: PB Cancel skips the night until it is over", cancelled != null && cancelled.state.cancelled && NightHandoverRule.holds(cancelled.state, n, false, at(200)))
        report.check("ps6a/night: AB Cancel is refused and changes nothing", NightHandoverRule.cancel(AB, ended.state, n, false, at(61)) == null)
        report.check("ps6a/night: a cancelled night stays cancelled when a session runs again", NightHandoverRule.step(cancelled!!.state, n, true, at(90)).state.cancelled)
        report.check("ps6a/night: the next night starts fresh (no cancel, no reminder shown)", NightHandoverRule.step(cancelled.state, "night-2", false, at(1440)).state == NightHandoverState(night = "night-2"))

        // A session that ended before the night makes no hand-over; one that starts again during the 5 minutes cancels the card.
        report.check("ps6a/night: no session during the night means no card", NightHandoverRule.step(idle, n, false, t0).effects.isEmpty() && NightHandoverRule.pendingStart(idle, n, false, t0) == null)
        val restart = NightHandoverRule.step(ended.state, n, true, at(62))
        report.check("ps6a/night: a new session during the 5 minutes holds the night and clears the card, but never moves the start (once per night)",
            restart.state.startAt == at(65) && NightHandoverEffect.CLEAR_COUNTDOWN in restart.effects && NightHandoverRule.holds(restart.state, n, true, at(62)))
        // Once per night: a second session inside the countdown ends before the start time: the same start time, shown again.
        val secondEnds = NightHandoverRule.step(restart.state, n, false, at(63))
        report.check("ps6a/night: a second session that ends inside the 5 minutes keeps the first start time (the card comes back, no new 5 minutes)",
            secondEnds.state.startAt == at(65) && NightHandoverEffect.SHOW_COUNTDOWN in secondEnds.effects && NightHandoverRule.pendingStart(secondEnds.state, n, false, at(63)) == at(65))
        // The start time has passed when the later session ends: the night starts at once.
        val laterRuns = NightHandoverRule.step(started.state, n, true, at(120))
        val laterEnds = NightHandoverRule.step(laterRuns.state, n, false, at(150))
        report.check("ps6a/night: a second session after the hand-over holds the night while it runs",
            NightHandoverRule.holds(laterRuns.state, n, true, at(120)) && laterRuns.state.startAt == at(65) && !laterRuns.state.done)
        report.check("ps6a/night: when it ends after the start time has passed, the night starts at once (no new 5 minutes, no card)",
            laterEnds.state.done && laterEnds.state.startAt == at(65) && NightHandoverEffect.NIGHT_STARTS in laterEnds.effects && NightHandoverEffect.SHOW_COUNTDOWN !in laterEnds.effects &&
                !NightHandoverRule.holds(laterEnds.state, n, false, at(150)))
        val laterEndsEarly = NightHandoverRule.step(NightHandoverRule.step(ended.state, n, true, at(61)).state, n, false, at(66))
        report.check("ps6a/night: same when the first session's start time passed while the second ran", laterEndsEarly.state.done && NightHandoverEffect.NIGHT_STARTS in laterEndsEarly.effects)
        // Test sessions never delay the night: the hold and the hand-over read real sessions only (a test session reads as no session, so the rule never sees it).
        val testOnly = NightHandoverRule.step(idle, n, sessionRunning = false, now = at(10))
        report.check("ps6a/night: a test session alone (no real session) holds nothing and hands over nothing",
            testOnly.effects.isEmpty() && !testOnly.state.sawSession && !NightHandoverRule.holds(testOnly.state, n, false, at(10)))
        val testDuringCountdown = NightHandoverRule.step(ended.state, n, false, at(62))
        report.check("ps6a/night: the test-session loop is closed: a test session inside the 5 minutes changes nothing, the night starts at the first start time",
            testDuringCountdown.state.startAt == at(65) && testDuringCountdown.effects.isEmpty() &&
                NightHandoverRule.step(testDuringCountdown.state, n, false, at(65)).effects.contains(NightHandoverEffect.NIGHT_STARTS))

        // Snooze cap: AB two Snoozes per night (5 minutes each), then none; PB uncapped.
        val s1 = NightHandoverRule.snooze(AB, ended.state, n, false, at(61))!!
        val s2 = NightHandoverRule.snooze(AB, s1.state, n, false, at(62))!!
        report.check("ps6a/night: AB allows two Snoozes per night, 5 minutes from each tap", s1.state.startAt == at(66) && s2.state.startAt == at(67) && s2.state.snoozes == 2)
        report.check("ps6a/night: AB's third Snooze is refused and the card no longer offers it", NightHandoverRule.snooze(AB, s2.state, n, false, at(63)) == null && !NightHandoverRule.canSnooze(AB, s2.state))
        report.check("ps6a/night: after the second Snooze the night starts at its time", NightHandoverRule.step(s2.state, n, false, at(67)).effects.contains(NightHandoverEffect.NIGHT_STARTS))
        var pb = ended.state
        var pbOk = true
        for (i in 1..6) {
            val next = NightHandoverRule.snooze(PB, pb, n, false, at(61L + i)) ?: run { pbOk = false; null }
            if (next != null) pb = next.state
        }
        report.check("ps6a/night: PB has no cap (six Snoozes in a row)", pbOk && pb.snoozes == 6 && NightHandoverRule.canSnooze(PB, pb))
        report.check("ps6a/night: a session between Snoozes does not give AB the Snoozes back", NightHandoverRule.step(s2.state, n, true, at(63)).state.snoozes == 2)
        report.check("ps6a/night: after the night is over everything resets", NightHandoverRule.step(started.state, null, false, at(600)).state == NightHandoverState())
    }

    private fun sources(report: CheckReport) {
        fun code(path: String) = Sync1cCheck.appSource(path)?.replace("\r\n", "\n")?.let(Sync1cCheck::code)
        val launch = code("essentials/EssentialsLaunch.kt")
        val lunchLift = code("essentials/LunchLockLift.kt")
        val lock = code("essentials/SessionLock.kt")
        val choice = code("essentials/LockScreenChoice.kt")
        val activity = code("essentials/EssentialsActivity.kt")
        val handover = code("essentials/NightHandover.kt")
        val refresher = code("notify/Refresher.kt")
        val curbox = code("curbox/CurboxHardMode.kt")
        val pane = code("ui/TodayPane.kt")

        report.check("ps6a/sources: the sources were found", listOf(launch, lunchLift, lock, choice, activity, handover, refresher, curbox, pane).all { it != null })
        report.check("ps6a/sources (order): runningSessionStart is own, then PC, then test",
            launch != null && launch.contains("OwnSessionEnforcement.runningStart(ownSessionStart(app, now), realSessionStart(app, now), app.testSession.running(now))"))
        val real = launch?.let { Sync1cCheck.body(it, "internal fun realSessionStart(", "\n    }\n") }
        report.check("ps6a/sources: realSessionStart stays PC-only (Phone down and the end cost read it)", real != null && !real.contains("ownSession") && !real.contains("phoneSession") && real.contains("KIND_PC"))
        report.check("ps6a/sources (B1): the own session is read from the store's flag, never compared with a wall clock",
            launch != null && launch.contains("app.phoneSession.running.value?.takeIf { it.isRunning && !it.isTest }") && !launch.contains("Instant.now()") && !launch.contains("System.currentTimeMillis"))
        report.check("ps6a/sources (B7c): sessionOnBreak is false during the own session",
            launch != null && launch.contains("OwnSessionEnforcement.onBreak(ownRunning(app, now), pcOnBreak(app, now))"))
        // B7b: the global hardcore profile and the unlock switch are read only inside lmpProfile, essentialsOnUnlock and the session-less plain lock.
        val offending = mutableSetOf<String>()
        var current = ""
        launch?.lines()?.forEach { line ->
            Regex("""fun (\w+)\(""").find(line)?.let { current = it.groupValues[1] }
            if (line.contains("essentials.hardcoreProfile") || line.contains("essentials.onUnlockInSession")) offending += current
        }
        report.check("ps6a/sources (B7b): EssentialsLaunch reads the global hardcore profile and unlock switch only in lmpProfile, essentialsOnUnlock and the session-less plain lock",
            offending == setOf("lmpProfile", "essentialsOnUnlock", "lockWithLockMyPhone"), offending.toString())
        report.check("ps6a/sources (B1): each helper hands the rule whether another device's session runs",
            launch != null && launch.contains("app.essentials.onUnlockInSession, otherSessionRunning(app, now))") && launch.contains("app.essentials.hardcoreProfile, otherSessionRunning(app, now))") &&
                launch.contains("ownProfile(app), globalGroup, otherSessionRunning(app, now))") && launch.contains("realSessionStart(app, now) != null || app.testSession.running(now) != null"))
        report.check("ps6a/sources (B7b, N1): the unlock, the hardcore start and the older START_LOCK path go through the helpers with their own now",
            launch != null && launch.contains("session && essentialsOnUnlock(app, now) ->") && launch.contains("!essentialsOnUnlock(app, now)) return") &&
                launch.contains("val profile = lmpProfile(app, now) ?: return\n") && launch.contains("val profile = lmpProfile(app, now) ?: return false"))
        report.check("ps6a/sources (B7b): the hold (SessionLock) uses the helpers, no global profile or switch",
            lock != null && lock.contains("EssentialsLaunch.lmpProfile(app, now)") && lock.contains("EssentialsLaunch.essentialsOnUnlock(app, now)") && !lock.contains("hardcoreProfile") && !lock.contains("essentials.onUnlockInSession"))
        report.check("ps6a/sources (S5): any session, the own one in either mode too, holds the lock choice (looser changes never settle in a session)",
            choice != null && choice.contains("fun locked(now: Instant = TrustedTime.now()): Boolean = EssentialsLaunch.runningSessionStart(app, now) != null"))
        report.check("ps6a/sources: the refresher counts the own session as guarded", refresher != null && refresher.contains("EssentialsLaunch.ownRunning(app,") && refresher.contains("val guarded = sessionRunning != null || testRunning || ownRunning"))

        // Curbox: S1 (global group only with hard mode on), S2 (trusted time), S3 (same-check restart after the key moved).
        report.check("ps6a/sources (B7b, S1): Curbox takes the group through the helper, and the global group only when hard mode is on",
            curbox != null && curbox.contains("EssentialsLaunch.curboxGroup(app, group(context).takeIf { enabled(context) }, now)") && !curbox.contains("!enabled(context)"))
        report.check("ps6a/sources (S2): Curbox hard mode runs on trusted time (default and the refresher's call)",
            curbox != null && curbox.contains("now: Instant = TrustedTime.now()") && !curbox.contains("Instant.now()") && refresher != null && refresher.contains("CurboxHardMode.check(context, com.accountabilibuddy.companion.guidance.TrustedTime.now())"))
        report.check("ps6a/sources (S3): when only the own session's key moved, Curbox starts the group again in the same check",
            curbox != null && curbox.contains("if (running == null || app.emergency.isActive(now)) return"))

        // D: the lunch lift decides on trusted time; N4: the lunch flag itself was always trusted (LunchStore ignores its argument).
        report.check("ps6a/sources (D): LunchLockLift reads no wall clock; check and testStop default to trusted time",
            lunchLift != null && !lunchLift.contains("Instant.now()") && !lunchLift.contains("System.currentTimeMillis") &&
                lunchLift.contains("fun check(context: Context, now: Instant = TrustedTime.now())") && lunchLift.contains("fun testStop(context: Context, now: Instant = TrustedTime.now())"))
        report.check("ps6a/sources (D): the lunch is read on the same trusted time the check was given, and the re-lock asks for the own session's profile",
            lunchLift != null && lunchLift.contains("val onLunch = app.lunch.isOnLunch(now)") && lunchLift.contains("val profile = EssentialsLaunch.lmpProfile(app, now)") && !lunchLift.contains("essentials.hardcoreProfile"))
        report.check("ps6a/sources (D): every caller of LunchLockLift.check passes TrustedTime.now() (no default-clock call)",
            Regex("""LunchLockLift\.check\(([^\n]*)\)""").findAll(listOf(refresher, code("CompanionApp.kt"), code("lunch/LunchStore.kt")).filterNotNull().joinToString("\n")).toList().let { calls ->
                calls.isNotEmpty() && calls.all { it.groupValues[1].contains("TrustedTime.now()") }
            })
        report.check("ps6a/sources (D): the refresher hands LunchLockLift.check the trusted time", refresher != null && refresher.contains("LunchLockLift.check(context, com.accountabilibuddy.companion.guidance.TrustedTime.now())"))
        report.check("ps6a/sources (D): none of the lock decisions (launch, hold, lift, choice, Curbox, the night hand-over) reads a wall clock",
            listOf(launch, lunchLift, lock, choice, curbox, handover).all { it != null && !it.contains("Instant.now()") && !it.contains("System.currentTimeMillis") && !it.contains("LocalDateTime.now()") })
        report.check("ps6a/sources: the built-in screen's reopen decision runs on trusted time (EssentialsActivity)",
            activity != null && !Regex("""runningSessionStart\(app, Instant\.now\(\)\)""").containsMatchIn(activity) &&
                activity.contains("runningSessionStart(app, com.accountabilibuddy.companion.guidance.TrustedTime.now())") &&
                activity.substringAfter("private fun shouldReopen()").substringBefore("val running").contains("val now = com.accountabilibuddy.companion.guidance.TrustedTime.now()"))

        // B2: the night.
        report.check("ps6a/sources (B2): a session takes over the night (unlock and the night lock), then the 5-minute hold",
            launch != null && launch.contains("val night = if (nightSessionRunning(app, now) || !nightAllowed(app) || NightHandover.holds(app, now)) null else BedtimeNight.current(app, NightHandover.local(now))") &&
                launch.contains("if (nightSessionRunning(app, now) || NightHandover.holds(app, now)) return") && launch.contains("NightHandover.check(context, now)"))
        report.check("ps6a/sources (B2): only a REAL session (own or a PC's) holds the night or hands it over; the test session never does",
            launch != null && launch.lines().any { it.trim() == "internal fun nightSessionRunning(app: CompanionApp, now: Instant): Boolean = ownRunning(app, now) || realSessionStart(app, now) != null" } &&
                handover != null && handover.contains("EssentialsLaunch.nightSessionRunning(app, now)") && !handover.contains("runningSessionStart") && !handover.contains("testSession"))
        report.check("ps6a/sources (B2): Snooze is capped by mode (the card and the notification offer it only while allowed)",
            handover != null && handover.contains("NightHandoverRule.snooze(EssentialsLaunch.mode(app), load(context)") && handover.contains("if (NightHandoverRule.canSnooze(EssentialsLaunch.mode(app), step.state)) add(action(context, ACTION_SNOOZE") &&
                code("ui/NightHandoverCard.kt")?.contains("if (shown.canSnooze)") == true)
        report.check("ps6a/sources (V-N1): the card looks at the night every 5 seconds only while it shows, a minute otherwise",
            code("ui/NightHandoverCard.kt")?.contains("delay(if (pending != null) 5_000L else 60_000L)") == true)
        report.check("ps6a/sources (B2): the night is looked up on trusted time", launch != null && !launch.contains("BedtimeNight.current(app)") && handover != null && handover.contains("BedtimeNight.current(app, local(now))"))
        report.check("ps6a/sources (B2): the refresher looks at the night on trusted time before it starts the night's lock, and wakes at the start time",
            refresher != null && refresher.indexOf("NightHandover.check(context, com.accountabilibuddy.companion.guidance.TrustedTime.now())") in 1 until refresher.indexOf("EssentialsLaunch.maybeStartBedtimeHardcore(context)") &&
                refresher.contains("untilHandover") && refresher.contains("untilGuidance, untilHandover"))
        report.check("ps6a/sources (B2): the 5 minutes end by asking for the night's lock again; Cancel is the rule's mode check",
            handover != null && handover.contains("NightHandoverEffect.NIGHT_STARTS -> EssentialsLaunch.forgetNightLock(context)") && handover.contains("NightHandoverRule.cancel(EssentialsLaunch.mode(app)") &&
                launch != null && launch.contains("internal fun forgetNightLock("))
        report.check("ps6a/sources (B2): the card rides with Today's banner on the phone and the tablet",
            pane != null && pane.contains("{ banner(); NightHandoverCard() }") && pane.contains("syncLight, shownBanner, actions") && pane.contains("banner = shownBanner"))
        report.check("ps6a/sources (B2): the notification buttons have their receiver in the manifest",
            Sync1cCheck.appSource("../../../AndroidManifest.xml")?.contains(".essentials.NightHandoverReceiver") == true ||
                java.io.File("app/src/main/AndroidManifest.xml").takeIf { it.isFile }?.readText()?.contains(".essentials.NightHandoverReceiver") == true ||
                java.io.File("../app/src/main/AndroidManifest.xml").takeIf { it.isFile }?.readText()?.contains(".essentials.NightHandoverReceiver") == true)
    }
}
