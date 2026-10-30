# 54. Per-device keys and re-keying on device removal

Date: 2026-10-30

## Status

Accepted

Amends [22. Share progress between your own devices through sealed files](0022-share-progress-between-your-own-devices-through-sealed-files.md)

## Context

Every device in a sync group shares one key. It is derived from the 40-character link code (`SyncKey.FromSecret`, HKDF over the code's 200 random bits), and a device that holds the code is accepted without any further check. There is no roster of devices, no way to remove one, and no way for a device to learn it has been dropped. The only exit is "Leave group" on the device itself. A device the user no longer uses therefore stays in every list as "Behind" forever. The phone also forwards other devices' files to every method it uses, so a dead device's file can come back after it is deleted.

The user stopped using a phone and wants to remove it from any of their other devices. They chose a real lock-out over a soft "ignore its file": removing a device must cut it off even if it still holds the old link code, which also covers a lost or sold phone. Rejoining must go through normal linking, which needs a code or QR from a device that is still linked.

The constraints are the existing ones. Sync stays end-to-end sealed, with nothing readable by Drive, a folder host or the relay (ADR 22, 26, 46). No new library is added. The PC (.NET) and the phone (Kotlin) must produce and read the same bytes. Older app versions in the group must not corrupt anything.

## Decision

We will give each device its own key pair and re-key the group whenever a device is removed.

Each device generates an ECDH P-256 key pair once and keeps the private key on the device: under DPAPI on the PC, and sealed by an Android Keystore key on the phone. It publishes the public key in its device card as an optional `PublicKey` field. P-256 is built into both .NET (`ECDiffieHellman`) and Android (`KeyPairGenerator("EC")`), so no dependency is needed.

Any linked device can remove another; the action is locked during a focus session the user started. The remover creates a new random link code and writes a rekey file (`ABR1`) to every sync method it uses. The file is sealed with the old group key. It names the removed devices and carries, for each remaining device that has a `PublicKey`, a copy of the new code wrapped for that device: ECDH with an ephemeral key, then AES-256-GCM. A remaining device unwraps its copy and switches to the new key in place. It keeps everything it has received, unlike leaving and joining, which forgets the group. It then refuses files from the removed ids and deletes its own old-group files where the method allows.

A removed device can still open the rekey file with the old key, so it learns that it was removed. It cannot unwrap the new code. It enters an Unlinked state, syncs nothing, and offers three choices with no default: add this device again (normal linking), continue unsynced here, or start from scratch.

The removed ids are kept on each device in a local document, `"sync.removed"`. Their files are refused there, and the phone never forwards them.

## Consequences

Removing a device now really cuts it off. A lost or sold phone loses access to new data the moment the others apply the rekey. The removed device is told why it stopped syncing instead of silently falling behind.

Devices running an app from before this change publish no public key, so they cannot receive the new code. Removing a device while such a device is in the group also cuts that one off. The remove dialog names those devices and suggests updating them first, or linking them again afterwards. This is the main mixed-version cost, and it lasts until every device runs 0.1.117 or later.

Data the removed device could already read stays readable to it. Re-keying protects only what is written after the switch. A device that is offline during the switch picks up the rekey file when it next syncs, as long as the file is still in the sync location. The relay drops untouched files after 30 days, so a device that is away longer has to be linked again.

The group now has key material that changes over time and must be kept straight across the PC and phone code. Getting it wrong could strand a device. So the rekey path gets an Opus-high review, and is checked on two PC copies and the phone over each sync method before release. The file format (`ABR1`) and the card field are now part of the sync contract and must stay readable by later versions.
