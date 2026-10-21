package com.accountabilibuddy.shared

/** The Data Layer names the phone and watch apps agree on (ADR 21). */
object WatchPaths {
    /** The phone sends the watch a [WatchSnapshot] as JSON. */
    const val SNAPSHOT = "/snapshot"

    /** The watch asks the phone for a fresh snapshot (sent when the watch app opens). */
    const val HELLO = "/hello"

    /** The phone tells the watch it's not linked (any more): the watch forgets what it has. */
    const val UNLINK = "/unlink"

    /** Advertised by the watch app (res/values/wear.xml), so the phone finds watches that have it. */
    const val WATCH_CAPABILITY = "accountabilibuddy_watch"

    /** Advertised by the phone app, so the watch finds the phone. */
    const val PHONE_CAPABILITY = "accountabilibuddy_phone"
}
