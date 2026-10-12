# ADR-0007: Answer on MSFS's default connection while MSFS is closed

Date: 2026-10-02. Status: superseded by ADR-0012 (the pipe is now only the fallback).

## Context
Until now, the ATC app reached ClearanceLink through two `SimConnect.cfg` files written into the ATC app's folder (one next to
the app's helper program, one next to the app's .exe) pointing at `tcp:5112`. That meant switching back and forth with
batch files, and it left the ATC app unable to reach MSFS while those files were in place. Without a cfg file, every
SimConnect client connects to MSFS's default named pipe, `\\.\pipe\Microsoft Flight Simulator\SimConnect`.

Tested on Ben's PC on 2026-10-02 (X-Plane 12.4.3, MSFS closed, no cfg files, `serve --listen pipe`): both ATC app
connections arrived on the pipe, the ATC app detected "MSFS 2024", loaded YBBN from Orbx scenery, showed the aircraft at its
gate and tuned COM1 in X-Plane. Other MSFS add-ons connected too as soon as the pipe was open (SayIntentions'
`FS2024BridgeService`, a `Request Data` client reading NG Academy L:vars). They were harmless: unknown requests are
logged and left unanswered.

## Decision
A new `connection` setting, `automatic` by default:
- **Automatic:** `serve` answers on the default pipe while the real MSFS is not running, plus `tcp:5112` for a
  the ATC app still set up with cfg files. Nothing in the ATC app's folder is changed. `MsfsGate` checks every 3 s for a
  `FlightSimulator2024` or `FlightSimulator` process that is not ClearanceLink's stand-in. While MSFS runs, ClearanceLink
  opens no pipe instance. If MSFS starts while ClearanceLink runs, ClearanceLink steps aside: the waiting pipe instance and
  every pipe client are closed and the stand-in is stopped. Both come back when MSFS closes.
- **Always on:** the cfg files are written, as before, and `serve` listens on `tcp:5112` only, so the ATC app goes to
  X-Plane even while MSFS is open.

Other MSFS add-ons that connect are shown on the status page as "Other MSFS add-on (...), not used" and do not count
as the ATC app.

## Alternatives
- **Cfg files only (before).** Needs switching, and breaks the ATC app for MSFS while ClearanceLink is closed.
- **Pipe only, never TCP.** Breaks anyone still set up with the cfg files from 0.3.0.
- **Turn other add-ons away.** They would retry and flood the log. Accepting and ignoring them was harmless in the test.

## Consequences
- Users only have to close MSFS before flying X-Plane. The status page says so when MSFS is running.
- Untested: MSFS starting while ClearanceLink is running. MSFS might check for a process with its own name before the
  stand-in is stopped; that needs a test on a PC with MSFS.
