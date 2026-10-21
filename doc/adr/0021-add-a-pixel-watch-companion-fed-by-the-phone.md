# 21. Add a Pixel Watch companion fed by the phone

Date: 2026-10-21

## Status

Accepted

## Context

The user wants a Pixel Watch app that sits alongside the phone and the PC and shows the running focus session, the time focused today, the week and the day's timeline, drawn as rings rather than the straight bars the PC and phone use. A first mockup on sample data was built to start from.

The project's hard rules keep everything local except a short list of connections. ADR 14 makes the PC the hub, with the phone as its paired device on the local network. A watch app raises three questions: where its code lives, what it may receive, and how the data reaches it.

Google's documentation for the Wearable Data Layer, the standard phone-to-watch channel in Google Play services, says it uses the Bluetooth connection when there is one and otherwise routes through a node on Google's servers. Data sent that way is end-to-end encrypted, but Google asks developers to assume it "may at some point use Google-owned servers". Google also says the Data Layer is the only supported way for a phone app and a watch app to talk, and that developers shouldn't open their own sockets between them.

## Decision

The watch app is a Wear OS app in `android/wear`, a second module of the Android project, with the phone app's application id and signature so Wear OS treats the two as one app. It uses Compose for Wear OS, a Tile and a watch-face complication; the user approved these libraries.

The watch receives a small snapshot and nothing else:
- the day plan's parts;
- today's focus sessions, including ones added by hand;
- drift times;
- the running session;
- focused and planned totals for today and the week.

These are the readable fields ADR 15 already allows paired devices to have. Window titles, addresses, OCR text and reasons are never sent.

The watch's data comes over the first of these that is turned on and works:

1. **Through the phone, over the Data Layer.** This is on by default, and so is Google's encrypted relay when Bluetooth is off.
2. **Through the phone, over the Data Layer, only while the watch is nearby (Bluetooth).** This is used when the relay is turned off. The phone checks before each send that the watch is nearby, and uses messages rather than stored items that would sync later.
3. **Straight to the PC over Wi-Fi.** The watch joins the ADR 14 link as its own paired device (kind "watch"). The pairing offer comes from the PC's QR code through the phone, because a QR code can't be scanned on a watch.
4. **A direct Bluetooth connection between the phone and watch apps,** as a last resort. Google advises against it, so it is built only if a test on the user's Pixel 7 and Pixel Watch shows it works next to the system's own Bluetooth link. Otherwise it is dropped and this ADR is amended.

Each of these can be turned off on the phone before a watch is linked, and later. Nothing is sent to a watch until the user links it on the phone.

## Consequences

The phone and the watch gain a Google Play services dependency (`play-services-wearable`). With the relay on, the watch snapshot can pass through Google's servers end-to-end encrypted. This is the first exception to "local only" for user data. It is the user's choice, on by default, and can be switched off. The PC gains a message that sends today's focus history to its paired devices (part of the linked-devices milestone L3), which the phone's planned Stats tab can use too. The project CLAUDE.md's list of allowed connections names the watch link. ADR 14 has the phone connect to the PC only while its app is open; when the watch app asks for news, the phone now also connects for a moment in the background to fetch the PC's session and today's focus, and only when an answer may actually reach the watch. Between connections the phone keeps the PC's last word on the running session, and the watch shows how old its data is. Today's focus follows ADR 15's switches: with Activity history off only the plan goes, and unpairing removes what was received. If the direct Bluetooth test fails, the fallback chain ends at Wi-Fi.
