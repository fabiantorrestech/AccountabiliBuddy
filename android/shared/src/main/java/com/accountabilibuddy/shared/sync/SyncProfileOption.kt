package com.accountabilibuddy.shared.sync

/** One of a PC's profiles as its file always lists them (ADR 42, "profileOptions"), for the phone's start picker: the id and the name only. */
data class SyncProfileOption(val id: String, val name: String)
