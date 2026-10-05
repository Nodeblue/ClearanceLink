# ADR-0011: Save the latest SimBrief plan for the FMS

Date: 2026-10-05. Status: accepted.

## Context
BeyondATC downloads the user's latest SimBrief plan itself; the aircraft's FMS needs the same plan typed or loaded.
Pasting SimBrief's ICAO route fails in the FMS (speed/level groups such as N0421F430 and runway suffixes are not
waypoints). Ben asked for the plan to come in through ClearanceLink.

## Decision
While `serve` runs (and with `clearancelink simbrief`), ClearanceLink asks SimBrief's public xml.fetcher API for the
latest plan every 2 minutes and, when it is new, saves SimBrief's X-Plane 11/12 download (`fms_downloads.xpe`, with
runways, SID and STAR) as `<X-Plane>/Output/FMS plans/<ORIG><DEST>.fms`. The pilot ID is read from BeyondATC's own
setting (Unity PlayerPrefs, `HKCU\Software\Skirmish Mode Games, Inc\BeyondATC`, DWORD `simbriefId_h<hash>`, seen on
Ben's PC), or `settings simbrief-id`. `settings simbrief-plans off` turns it off. STATUS shows `FMS PLAN <name> SAVED`
in turn before BeyondATC connects.

## Alternatives
- **Parse the ICAO route string ourselves.** Needs airway expansion from X-Plane's nav data; SimBrief already did it.
- **A pilot ID row on SETUP.** Kept as the settings fallback only: BeyondATC already has the ID, and SETUP is full.

## Consequences
- One small HTTPS request to SimBrief every 2 minutes while ClearanceLink runs.
- Not yet checked: that each add-on aircraft's FMS lists `Output/FMS plans` (X-Plane's own FMS and most add-ons do).
