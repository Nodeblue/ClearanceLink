# ADR-0012: Point the ATC app at ClearanceLink only while it is open

Date: 2026-10-05. Status: accepted (Ben to confirm on the draft PR).

## Context
ADR-0007 answered the ATC app on MSFS's default SimConnect pipe while MSFS was closed. Every SimConnect client on the
PC uses that pipe, so apps that support both MSFS and X-Plane saw an MSFS that was partly there. Ben reported on
2026-10-05 that SPAD.neXt gets confused by it, warns that its MSFS L:var bridge is not responding, and works or not
depending on what starts first. SayIntentions' `FS2024BridgeService` and an NG Academy reader were already seen
connecting on 2026-10-02.

## Decision
Connection **automatic** (the default) now uses the two `SimConnect.cfg` files from ALWAYS ON, but only while
ClearanceLink runs:
- `serve` listens on `tcp:5112` only and leaves MSFS's pipe alone.
- The files are written when serve starts (before the MSFS stand-in starts) and whenever MSFS closes. They are
  removed when MSFS starts (the old step-aside) and when serve stops. A user's own `SimConnect.cfg` is backed up and
  put back, as with ALWAYS ON.
- A guard process (`ClearanceLink app-guard --parent <pid>`, the same exe) waits for ClearanceLink to end and removes
  the files if it ended without doing so (a crash, or killed). It leaves them when always on was chosen in the
  meantime, or when another ClearanceLink already listens on 5112.
- SETUP's bottom line says `APP: X-PLANE TILL EXIT` while the files are in place.
- When the ATC app's folder is unknown or can't be written (a protected folder), serve falls back to ADR-0007.

ALWAYS ON is unchanged: the files stay after ClearanceLink closes.

## Alternatives
- **Answer only the ATC app on the default pipe.** A pipe can't be hidden from some processes: ClearanceLink only
  learns who connected (pipe client process, or the client name in SimConnect
  Open) after the client's `SimConnect_Open` has already succeeded. Dropping other clients then
  looks to them like MSFS starting and quitting, again and again, which is the confusion we want to avoid, not
  "SimConnect is not running".
- **Stop listening on the pipe once the ATC app has connected.** Other apps would then get "pipe busy" instead of a
  half-there MSFS, but only after the ATC app connects; an app started first still sees the pipe. Order-dependent,
  which is what Ben reported.
- **ALWAYS ON for everyone.** The ATC app could not reach MSFS while ClearanceLink was closed, and users would have to
  remember to switch.

## Consequences
- Other MSFS add-ons see no MSFS while flying X-Plane; OTHER ADD-ONS on STATUS stays at NONE in automatic mode.
- MSFS must still be closed while flying X-Plane in automatic mode, as before.
- A power cut or a guard that is killed with ClearanceLink (End process tree) leaves the files; the ATC app then can't
  reach MSFS until ClearanceLink is started and closed once. help.md says so.
- A second ClearanceLink.exe process runs for the whole session as the guard.
