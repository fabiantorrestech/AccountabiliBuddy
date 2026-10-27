# 45. Delete the Wi-Fi link

Date: 2026-10-27

## Status

Accepted

Supersedes [14. Pair the phone with the PC over the local network](0014-pair-the-phone-with-the-pc-over-the-local-network.md)

Amends [42. Control PC sessions and plan edits through sync files](0042-control-pc-sessions-and-plan-edits-through-sync-files.md) (its "separate step" of deleting the link), and retires the link parts of 15, 19, 21 and 26.

## Context

ADR 14 paired the phone with the PC over a pinned-TLS connection on the local network, and ADR 21 let the watch reach the PC the same way when the phone was away. ADR 26 made Google Drive the way devices sync and kept the link only behind an "old way" switch. ADR 42 then moved the last four things that needed the link into the sealed sync files: starting a PC session from the phone, ending one early with the PC's wait, the phone's day plan edits, and the phone's live view of the PC. The watch's PC noise control (W 1) went through the phone and the sync files from the start.

The link now carries nothing the files don't. It still costs a listening port on the PC (with Windows' firewall prompt and a public-network notice), a pairing flow and its secrets on three devices, a second code path in every phone feature that reads the PC's state, and its own bugs. The user approved deleting it, including the watch's PC route, on 2026-10-27.

## Decision

We will delete the Wi-Fi link entirely. That covers the PC's server and discovery responder, the pairing (QR and pasted links), the stored identity, tokens and paired-device list, the phone's client and its pairing UI, the watch's own PC client and its Wi-Fi fallback, and the "old way" switch. Every device talks to the others only through the sealed sync files (ADR 22, 26, 42). The watch reaches a PC only through the phone.

Types that the link packages hold but the sync files use (the JSON options, the day plan edit and end-early terms, the PC state the phone reads, and the Keystore-backed token vault that seals the sync group code, whose alias stays the same) move to the sync packages unchanged. The stored link secrets are deleted once, on the first start after the upgrade. Old sync files that name "link" as a delivery method are still read.

## Consequences

The PC no longer listens on the network, so there is no firewall prompt, no public-network pause and one less attack surface. Pairing, its secrets and the link-or-files branches in phone and watch code disappear, which makes those features simpler to reason about and to change.

Control gets slower and depends on a sync method. A phone action reaches the PC after a Drive upload and the PC's poll: about 10 seconds in a session or a work part, up to 5 minutes otherwise. The phone sees the PC's state every 30 seconds in a session, else every 15 minutes. Without Google Drive (or the self-hosted relay planned in phase 6), there is no PC-phone control at home at all, where the link used to work on Wi-Fi alone. The watch loses its route to the PC when the phone is out of reach. This is a breaking change: an old phone can't link to a new PC, nor a new phone to an old PC, though both still sync through files. Bringing a direct local connection back later would mean a new pairing design, not a revert.
