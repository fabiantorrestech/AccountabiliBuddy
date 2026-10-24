# 36. Control the phone's noise player from the watch

Date: 2026-10-24

## Status

Accepted

Amends [21. Add a Pixel Watch companion fed by the phone](0021-add-a-pixel-watch-companion-fed-by-the-phone.md)

## Context

The phone plays focus noise from a folder of sounds. The user wants to control it from the Pixel Watch (2026-10-24): play and pause, skip, and the volume with the crown, without taking the phone out. Until now the watch only received a read-only snapshot and asked for news; it could start, end or change nothing.

## Decision

The watch app gets a Noise page. The phone sends the linked watch what its noise player is doing ("/noise": playing, the sound's shown name, the volume, whether a folder is chosen) over the Wearable Data Layer when it changes and when the watch says hello, by the same route and switches as the snapshot. The watch sends commands back ("/noise-command": play/pause, next, previous, volume 0 to 100). The phone applies them only from the linked watch. On the Noise page the crown changes the volume by 5% a step. Only the phone's noise player can be controlled this way; the PC route (ADR 21) carries no noise, and the watch still can't start, end or change a focus session or any setting.

## Consequences

The user can steer the noise from the wrist. A sound's shown name now reaches the watch, over the Data Layer only, like the rest of the snapshot. Commands need the phone reachable: in Bluetooth range, or through Google's relay when the phone allows it.
