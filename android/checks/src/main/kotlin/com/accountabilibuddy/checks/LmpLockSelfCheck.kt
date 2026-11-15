package com.accountabilibuddy.checks

import com.accountabilibuddy.companion.essentials.CapabilityMemory
import com.accountabilibuddy.companion.essentials.HoldRules
import com.accountabilibuddy.companion.essentials.LooserChangeRules
import com.accountabilibuddy.companion.essentials.LooserChangeRules.Decision
import com.accountabilibuddy.companion.essentials.LooserChangeRules.Settle
import com.accountabilibuddy.companion.essentials.RedirectRules
import com.accountabilibuddy.companion.essentials.SigningCertificates
import java.time.Duration
import java.time.Instant
import java.time.ZoneOffset

/** The pure rules behind the lmp-lock review fixes S1-S6 and the download's redirect check. */
fun lmpLockSelfCheck(report: CheckReport) {
    val t0 = Instant.parse("2026-11-15T16:00:00Z")
    fun at(minutes: Long) = t0.plus(Duration.ofMinutes(minutes))

    // S6: the wait rule (trusted time is whatever "now" the caller passes; the rule never reads a clock itself).
    report.check("wait: stricter applies at once", LooserChangeRules.request(false, false) == Decision.Applied)
    report.check("wait: stricter applies at once, even in a session", LooserChangeRules.request(false, true) == Decision.Applied)
    report.check("wait: looser outside a session waits", LooserChangeRules.request(true, false) == Decision.Waits)
    report.check("wait: looser in a session is refused", LooserChangeRules.request(true, true) == Decision.Refused)
    val due = LooserChangeRules.dueAt(t0).toEpochMilli()
    report.check("wait: due is 10 minutes on", due - t0.toEpochMilli() == 600_000L)
    report.check("wait: not due at 9m59s", LooserChangeRules.settle(due, false, t0.plusSeconds(599)) == Settle.Keep)
    report.check("wait: applies at 10m", LooserChangeRules.settle(due, false, at(10)) == Settle.Apply)
    report.check("wait: applies after 10m", LooserChangeRules.settle(due, false, at(60)) == Settle.Apply)
    report.check("wait: a session cancels it", LooserChangeRules.settle(due, true, at(5)) == Settle.Cancel)
    report.check("wait: a session cancels it even once due", LooserChangeRules.settle(due, true, at(30)) == Settle.Cancel)
    report.check("wait: a trusted time that stays back keeps it waiting", LooserChangeRules.settle(due, false, at(-120)) == Settle.Keep)
    report.check("profile: own strict profile is not looser", !LooserChangeRules.profileLoosens("Mine", "Accountabilibuddy", "Accountabilibuddy"))
    report.check("profile: any other pick is looser", LooserChangeRules.profileLoosens("Accountabilibuddy", "Mine", "Accountabilibuddy"))
    report.check("profile: the same profile is no change", !LooserChangeRules.profileLoosens("Mine", "Mine", "Accountabilibuddy"))
    report.check("wait: shown as 12-hour", LooserChangeRules.clockText(Instant.parse("2026-11-15T16:05:00Z"), ZoneOffset.UTC) == "4:05 PM")
    report.check("wait: 12-hour after midnight", LooserChangeRules.clockText(Instant.parse("2026-11-15T00:05:00Z"), ZoneOffset.UTC) == "12:05 AM")

    // S2: the hold is sent again.
    val end = at(60).toEpochMilli()
    fun due(heldFor: String? = "A", heldUntil: Long = end, until: Long = end, setEnd: Boolean = true, sent: Long = t0.toEpochMilli(), now: Instant = at(1), force: Boolean = false) =
        HoldRules.due(heldFor, "A", heldUntil, until, setEnd, sent, now, force)
    report.check("hold: nothing to do a minute after a send", !due())
    report.check("hold: sent at once when not yet held", due(heldFor = null))
    report.check("hold: a new phase is sent", due(heldFor = "B"))
    report.check("hold: a moved end is sent", due(until = end + 60_000))
    report.check("hold: re-sent 5 minutes after the last send", due(now = at(5)))
    report.check("hold: not re-sent at 4m59s", !due(now = t0.plusSeconds(299)))
    report.check("hold: an unlock forces a re-send", due(force = true))
    report.check("hold: never sent before means send", due(sent = 0L))
    report.check("hold: a send time in the future (clock moved back) means send", due(sent = at(30).toEpochMilli()))
    report.check("hold: a lease is renewed with under 15 minutes left", due(setEnd = false, heldUntil = at(10).toEpochMilli(), until = at(40).toEpochMilli(), now = at(1)))
    report.check("hold: a fresh lease is not renewed", !due(setEnd = false, heldUntil = at(30).toEpochMilli(), until = at(30).toEpochMilli(), now = at(1)))

    // S3: one missed capabilities query keeps the last good answer for a bounded time.
    val memory = CapabilityMemory<String>(Duration.ofMinutes(15))
    report.check("capabilities: nothing remembered at first", memory.recall(t0, 0L) == null)
    memory.record("v2", t0, 1_000L)
    report.check("capabilities: kept through a miss", memory.recall(at(1), 61_000L) == "v2")
    report.check("capabilities: kept at 14m", memory.recall(at(14), 1_000L + 14 * 60_000L) == "v2")
    report.check("capabilities: gone at 15m (trusted)", memory.recall(at(15), 1_000L + 14 * 60_000L) == null)
    report.check("capabilities: gone at 15m (monotonic)", memory.recall(at(14), 1_000L + 15 * 60_000L) == null)
    report.check("capabilities: a clock moved back can't stretch it", memory.recall(at(-600), 1_000L + 20 * 60_000L) == null)
    report.check("capabilities: a clock moved forward can't stretch it", memory.recall(at(600), 61_000L) == null)
    memory.forget()
    report.check("capabilities: forgotten when LockMyPhone-Extended goes", memory.recall(at(1), 61_000L) == null)

    // S4 and the download: only GitHub's own addresses, https; and the certificate rule.
    val apk = "https://github.com/fabiantorrestech/android-apps/releases/download/lockmyphoneextended/LockMyPhoneExtended.apk"
    report.check("redirect: the release URL is allowed", RedirectRules.allowed(apk))
    report.check("redirect: objects.githubusercontent.com is allowed", RedirectRules.allowed("https://objects.githubusercontent.com/github-production-release-asset/1?X=2"))
    report.check("redirect: release-assets.githubusercontent.com is allowed", RedirectRules.allowed("https://release-assets.githubusercontent.com/github-production-release-asset/1440063961/x?sp=r"))
    report.check("redirect: host case is ignored", RedirectRules.allowed("https://GitHub.com/x"))
    report.check("redirect: http is refused", !RedirectRules.allowed("http://github.com/x"))
    report.check("redirect: another host is refused", !RedirectRules.allowed("https://evil.example/x"))
    report.check("redirect: a look-alike host is refused", !RedirectRules.allowed("https://github.com.evil.example/x"))
    report.check("redirect: a subdomain is refused", !RedirectRules.allowed("https://x.objects.githubusercontent.com/x"))
    report.check("redirect: user info is refused", !RedirectRules.allowed("https://github.com@evil.example/x"))
    report.check("redirect: another port is refused", !RedirectRules.allowed("https://github.com:8443/x"))
    report.check("redirect: garbage is refused", !RedirectRules.allowed("not a url"))
    report.check("redirect: a relative Location resolves", RedirectRules.resolve("https://github.com/a/b", "/c/d") == "https://github.com/c/d")
    report.check("redirect: a blank Location is nothing", RedirectRules.resolve("https://github.com/a", " ") == null)
    val key = SigningCertificates.sha256Hex(byteArrayOf(1, 2, 3))
    report.check("certificate: sha256 is 64 hex digits", key.length == 64 && key.all { it in "0123456789abcdef" })
    report.check("certificate: the same key matches", SigningCertificates.matches(setOf(key), setOf(key)))
    report.check("certificate: a different key is refused", !SigningCertificates.matches(setOf(SigningCertificates.sha256Hex(byteArrayOf(9))), setOf(key)))
    report.check("certificate: no certificate is refused", !SigningCertificates.matches(emptySet(), emptySet()))
    report.check("certificate: an extra signer is refused", !SigningCertificates.matches(setOf(key, "ff"), setOf(key)))
}
