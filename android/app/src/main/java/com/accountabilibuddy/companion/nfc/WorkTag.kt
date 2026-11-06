package com.accountabilibuddy.companion.nfc

import android.app.Activity
import android.nfc.NdefMessage
import android.nfc.NdefRecord
import android.nfc.NfcAdapter
import android.nfc.Tag
import android.nfc.tech.Ndef
import android.nfc.tech.NdefFormatable
import android.os.Handler
import android.os.Looper

/**
 * The finished-work tag (ADR 67): an NDEF external record "com.accountabilibuddy.companion:worktag" holding [WorkTagStore]'s secret.
 * Read only through reader mode while a screen that asks for it is in front ([enable]/[disable]): there is no intent filter, so the
 * tag does nothing anywhere else.
 */
object WorkTag {
    private const val DOMAIN = "com.accountabilibuddy.companion"
    private const val TYPE = "worktag"
    private const val READER_FLAGS = NfcAdapter.FLAG_READER_NFC_A or NfcAdapter.FLAG_READER_NFC_B or
        NfcAdapter.FLAG_READER_NFC_F or NfcAdapter.FLAG_READER_NFC_V

    /** The phone has NFC hardware. */
    fun isAvailable(activity: Activity): Boolean = NfcAdapter.getDefaultAdapter(activity) != null

    /** NFC is turned on in Android's settings. */
    fun isOn(activity: Activity): Boolean = NfcAdapter.getDefaultAdapter(activity)?.isEnabled == true

    /** Starts reading tags for [activity] while it is in front; [onTag] runs on the main thread. False without NFC. */
    fun enable(activity: Activity, onTag: (Tag) -> Unit): Boolean {
        val adapter = NfcAdapter.getDefaultAdapter(activity) ?: return false
        val main = Handler(Looper.getMainLooper())
        return runCatching {
            adapter.enableReaderMode(activity, { tag -> main.post { onTag(tag) } }, READER_FLAGS, null)
            true
        }.getOrDefault(false)
    }

    fun disable(activity: Activity) {
        runCatching { NfcAdapter.getDefaultAdapter(activity)?.disableReaderMode(activity) }
    }

    /** The secret on [tag]; null when it carries no finished-work record. */
    fun read(tag: Tag): String? {
        val message = runCatching { Ndef.get(tag)?.cachedNdefMessage }.getOrNull() ?: return null
        return message.records.firstOrNull { it.tnf == NdefRecord.TNF_EXTERNAL_TYPE && String(it.type) == "$DOMAIN:$TYPE" }
            ?.payload?.let { String(it) }
    }

    /** Writes [secret] to [tag] (formatting a blank one) and reads it back; null when done, else why not. Blocking: call off the main thread. */
    fun write(tag: Tag, secret: String): String? {
        val message = NdefMessage(NdefRecord.createExternal(DOMAIN, TYPE, secret.toByteArray()))
        return runCatching {
            val ndef = Ndef.get(tag)
            if (ndef != null) {
                ndef.connect()
                ndef.use {
                    if (!it.isWritable) return "This tag is read-only. Use a writable tag (NTAG213, 215 or 216)."
                    if (it.maxSize < message.byteArrayLength) return "This tag is too small. Use an NTAG213 or larger."
                    it.writeNdefMessage(message)
                    if (it.ndefMessage?.records?.any { record -> String(record.payload) == secret } != true) return "The tag didn't keep it. Try again."
                }
                null
            } else {
                val formatable = NdefFormatable.get(tag) ?: return "This tag can't hold the app's code. Use an NTAG213, 215 or 216."
                formatable.connect()
                formatable.use { it.format(message) }
                null
            }
        }.getOrElse { "Hold the tag still against the phone's back until it says done, then try again." }
    }
}
