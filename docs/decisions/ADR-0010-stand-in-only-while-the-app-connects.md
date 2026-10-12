# ADR-0010: The MSFS stand-in runs only while the ATC app connects

Date: 2026-10-05. Status: accepted.

## Context
The ATC app will not start unless a process named `FlightSimulator2024` runs, so ClearanceLink starts a do-nothing
stand-in with that name (ADR-0007). On Ben's PC, FlightSimBuilder HUB (cockpit hardware software that supports MSFS
and X-Plane) will not make a new connection to X-Plane while that process runs; it keeps a connection it already has.
Ben found that the ATC app only needs the process when it connects (first start or reopen); with it closed mid-flight,
the ATC app carried on.

## Decision
ClearanceLink starts the stand-in as before, closes it once the ATC app's aircraft-and-radios and airport-data connections (the helper program's connection,
or from 1.8.22 the app's own airport-data connection; see the update below) have been open for 5 s, and starts it again within about a second when either drops. While
the real MSFS runs nothing changes (no stand-in). `StubLauncher`, checked once a second by `serve`.

## Alternatives
- **Start the stand-in 15 s after X-Plane answers.** Lets add-ons connect first, but one that drops later (X-Plane
  restarted) cannot reconnect, and the ATC app must wait.
- **Ask users to start such add-ons before ClearanceLink.** Ben's workaround; easy to forget and breaks on any reconnect.

## Consequences
- Add-ons like FlightSimBuilder HUB cannot make a new X-Plane connection only during the few seconds the ATC app connects.
- If a later the ATC app version checks for the process during a flight, it would see the sim as closed; the live log
  shows `stub: stopped ... while the ATC app is connected` to match against the ATC app's Player.log.

## Update 2026-10-07 (the app's 1.8.22)
1.8.22 has no helper connection; it opens three connections of its own: aircraft and radios, airport data and traffic
timing.
ClearanceLink 0.7.4 waited for the old helper, so the stand-in never closed. From 0.7.5 the ATC app counts as connected
with its aircraft-and-radios connection plus either airport-data connection. 1.8.22 checks for the process only when Fly is
pressed, and then only as a first guess; the Open reply decides which sim it is, so closing the stand-in after it
connects is still safe.
