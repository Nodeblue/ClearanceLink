# ADR-0011: Save the latest SimBrief plan for the FMS

Date: 2026-10-05. Status: accepted.

## Context
BeyondATC downloads the user's latest SimBrief plan itself; the aircraft's FMS needs the same plan typed or loaded.
Pasting SimBrief's ICAO route fails in the FMS (speed/level groups such as N0421F430 and runway suffixes are not
waypoints). Ben asked for the plan to come in through ClearanceLink.

## Decision
A FPLN page in the window works like a jet's FMC route uplink. REQUEST asks SimBrief's public xml.fetcher API for the
latest plan and shows it (origin-destination, runways, SID/STAR, route, cruise) without touching X-Plane. LOAD, pressed
twice because it replaces the FMS's plan, saves SimBrief's X-Plane 11/12 download (`fms_downloads.xpe`, with runways,
SID and STAR) as `<X-Plane>/Output/FMS plans/<ORIG><DEST>.fms` and asks the plugin (0.5.3+, `fms` message) to load it
into X-Plane's own FMS with `XPLMLoadFMSFlightPlan`. Aircraft with their own FMS ignore that and load the saved file
from their menus. The pilot ID is read from BeyondATC's own setting (Unity PlayerPrefs,
`HKCU\Software\Skirmish Mode Games, Inc\BeyondATC`, DWORD `simbriefId_h<hash>`, seen on Ben's PC), or
`settings simbrief-id`. STATUS mentions FLIGHT PLAN READY: FPLN in turn while a received plan is not loaded.

## Alternatives
- **Parse the ICAO route string ourselves.** Needs airway expansion from X-Plane's nav data; SimBrief already did it.
- **A pilot ID row on SETUP.** Kept as the settings fallback only: BeyondATC already has the ID, and SETUP is full.
- **Save every new plan automatically (built first).** Ben preferred a button: the user chooses when the FMS changes.

## Consequences
- SimBrief is only asked when the user presses REQUEST.
- Not yet checked: that each add-on aircraft's FMS lists `Output/FMS plans` (X-Plane's own FMS and most add-ons do).
