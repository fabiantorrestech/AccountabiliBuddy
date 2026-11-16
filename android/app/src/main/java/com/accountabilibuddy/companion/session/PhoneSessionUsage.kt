package com.accountabilibuddy.companion.session

import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.model.LocalWindow
import com.accountabilibuddy.companion.usage.PhoneUsageReader
import com.accountabilibuddy.shared.session.PhoneSessionRecord
import com.accountabilibuddy.shared.session.PhoneSessionUsageRules
import com.accountabilibuddy.shared.session.PhoneSessionUsageSummary
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * Reads what the phone's own use looked like inside a phone session (ADR 85, plan PS2 (3)): the unlocks from Android's usage events and the time in the profile's
 * distracting apps. The rules are [PhoneSessionUsageRules] (pure). Needs Usage access: without it every number is null, shown as a dash and never as a zero.
 * The result stays on this phone: the session log only, never the sync file (R14). Blocking (IPC to the usage service): never call it from the main thread or
 * from under the controller's lock; [PhoneSessionRuntime] reads it off-thread when a session ends, and PS3a reads it for the session card.
 */
object PhoneSessionUsage {
    /** The summary of [record] up to [end] (the session's end, or the session clock's now for a running one). */
    fun read(app: CompanionApp, record: PhoneSessionRecord, end: Instant): PhoneSessionUsageSummary {
        val reader = PhoneUsageReader(app)
        val unlocks = reader.unlocks(record.startedAt, end)
        val apps = record.snapshot.distractingApps.toSet()
        val distraction = if (unlocks == null || apps.isEmpty()) null else distractionSeconds(reader, record, end, apps)
        return PhoneSessionUsageRules.summarize(record, end, unlocks, distraction)
    }

    // The time in the named apps inside the focus stretches: the usage reader already cuts every app's windows by the focus stretches it is given.
    private fun distractionSeconds(reader: PhoneUsageReader, record: PhoneSessionRecord, end: Instant, apps: Set<String>): Long? {
        val zone = ZoneId.systemDefault()
        fun local(at: Instant): LocalDateTime = LocalDateTime.ofInstant(at, zone)
        val spans = PhoneSessionUsageRules.focusSpans(record, end).map { LocalWindow(local(it.from), local(it.to)) }
        if (spans.isEmpty()) return 0L
        val usage = reader.read(LocalWindow(local(record.startedAt), local(end)), spans)
        return usage.apps.filter { it.packageName in apps }.sumOf { it.inFocus.seconds }
    }
}
