# ADR-0010: The MSFS stand-in runs only while BeyondATC connects

Date: 2026-10-05. Status: accepted.

## Context
BeyondATC will not start unless a process named `FlightSimulator2024` runs, so ClearanceLink starts a do-nothing
stand-in with that name (ADR-0007). On Ben's PC, FlightSimBuilder HUB (cockpit hardware software that supports MSFS
and X-Plane) will not make a new connection to X-Plane while that process runs; it keeps a connection it already has.
Ben found that BeyondATC only needs the process when it connects (first start or reopen); with it closed mid-flight,
BeyondATC carried on.

## Decision
ClearanceLink starts the stand-in as before, closes it once both BeyondATC connections (`BeyondATC` and
`BATCDLLSimConnection`) have been open for 5 s, and starts it again within about a second when either drops. While
the real MSFS runs nothing changes (no stand-in). `StubLauncher.FollowBatc`, checked once a second by `serve`.

## Alternatives
- **Start the stand-in 15 s after X-Plane answers.** Lets add-ons connect first, but one that drops later (X-Plane
  restarted) cannot reconnect, and BeyondATC must wait.
- **Ask users to start such add-ons before ClearanceLink.** Ben's workaround; easy to forget and breaks on any reconnect.

## Consequences
- Add-ons like FlightSimBuilder HUB cannot make a new X-Plane connection only during the few seconds BeyondATC connects.
- If a later BeyondATC version checks for the process during a flight, it would see the sim as closed; the live log
  shows `stub: stopped ... while BeyondATC is connected` to match against BeyondATC's Player.log.

## Update 2026-10-07 (BeyondATC 1.8.22)
1.8.22 has no `BeyondATC` helper connection; it opens `BATCDLLSimConnection`, `BATC_Facilities` and `BATC_TrafficSync`.
ClearanceLink 0.7.4 waited for the old helper, so the stand-in never closed. From 0.7.5 BeyondATC counts as connected
with `BATCDLLSimConnection` plus `BeyondATC` or `BATC_Facilities`. 1.8.22 checks for the process only when Fly is
pressed, and then only as a first guess; the Open reply decides which sim it is, so closing the stand-in after it
connects is still safe.
