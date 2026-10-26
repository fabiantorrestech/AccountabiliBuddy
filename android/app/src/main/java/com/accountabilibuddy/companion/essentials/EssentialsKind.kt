package com.accountabilibuddy.companion.essentials

/**
 * The two essentials lists: Focus (sessions on another device, emergency time, the work card) and Bedtime (from
 * bedtime until the wake time). Each has its own prefs, its own LockMyPhone-Extended profile kept in step with it,
 * and its own profile to start.
 *
 * @property prefs Where the list and its settings are kept.
 * @property lockMyPhoneSlot The slot its synced profile has in LockMyPhone-Extended (its ADR 0010); null = the
 *   app's original profile.
 * @property lockMyPhoneName The synced profile's name when LockMyPhone-Extended first creates it.
 */
enum class EssentialsKind(val prefs: String, val lockMyPhoneSlot: String?, val lockMyPhoneName: String) {
    Focus("essentials", null, "Accountabilibuddy"),
    Bedtime("bedtimeEssentials", "bedtime", "Accountabilibuddy Bedtime"),
}
