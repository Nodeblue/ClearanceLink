# Status

_Update this file in every PR that changes behaviour._

**Phase:** 1, answering BATC from X-Plane. Phase 0 (feasibility) passed.

## Works
- SimConnect packet framing, function and message names, and decoding of the common ATC-client calls (`src/XPConnect.Protocol`).
- `XPConnect record`: pass-through proxy that records a real client/sim session to JSONL and writes a call inventory.
- `XPConnect fake`: answers the Open handshake as MSFS 2020 or 2024 and serves the user aircraft's SimVars from a fixed snapshot (SF50 parked at YBBN, live clock, COM set events applied). `--facilities <recording.jsonl>` replays the airport data MSFS sent in a pass-through recording. Everything else is recorded and left unanswered.
- `src/XPConnect.SimStub` builds `FlightSimulator2024.exe`, the do-nothing process BATC needs to see before it connects. A console process with no window passes.
- `XPConnect serve` (first real session 2026-09-30, X-Plane 12.4.3, Phenom 300 at YBBN gate 24: BATC connected, showed the aircraft at the right gate, gave clearance, tuned COM1 in the X-Plane cockpit for ATIS, Clearance and Ground, and followed the aircraft while taxiing; X-Plane's own ATIS/ATC audio overlaps BATC's unless its radio volume is turned down): stands in for MSFS 2024 on `tcp:5112`, starts the `FlightSimulator2024` stub, and answers BATC from X-Plane 12's Web API (`localhost:8086`) through `mapping/simvars.yaml` and `mapping/events.yaml`. COM and transponder events from BATC are written to X-Plane. Aircraft data is held back until X-Plane reports a position. `--facilities` replays recorded airport data as in `fake`. Runbook: `docs/runbooks/phase1-xplane.md`.
- `XPConnect xplane-check`: reads every mapped dataref from a running X-Plane and prints what BATC would get, flagging datarefs X-Plane does not have. Mappings marked `verify: true` still need this check.
- `XPConnect summarize`: builds the call inventory from a recording.
- Verified against an independent client (node-simconnect 4.2) over TCP, on both the SunRise and KittyHawk protocols.
- Recorded a real BATC session against MSFS 2024 (YBBN to YTWB, 2026-09-30). BATC is redirected by a `SimConnect.cfg` next to `simconnect_ws.exe` and another next to `BeyondATC.exe`. Findings: `docs/research/batc-call-inventory.md`.
- `ServerIdentity.Msfs2024` matches the recorded Open reply (SunRise 12.2.282174.999, SimConnect 12.2.0.0).
- Stand-in test passed (2026-09-30): with MSFS closed, BATC connects both SimConnect connections to `fake --identity msfs2024` and accepts the handshake, provided a process named `FlightSimulator2024.exe` is running (a do-nothing stub passes). With user-aircraft data and replayed facility data served, BATC loads YBBN and the flight plan, gives IFR clearance, tunes COM1 through us and issues a taxi route, all without MSFS (2026-09-30).
- Recordings sample high-rate streams (AI position writes, SimObjectData) to one per second per stream by default (`--throttle-ms`). A full-rate 30-minute flight was 2.9 GB.

## Not yet verified
- `serve` against a running X-Plane 12: dataref names and units marked `verify: true` in `mapping/simvars.yaml` (pressure altitude, wind, transponder mode, aircraft title), and the Web API message shapes, which follow X-Plane's documentation and a fake in the tests.
- AI traffic stays off: `serve` does not answer EnumerateSimObjectsAndLiveries, and in the stand-in test BATC created no AI aircraft without it.
- BATC's `simconnect_ws.exe` connecting to our named pipe on Windows (message-mode pipe, security).
- BATC's behaviour in flight without MSFS (reconnects, any checks later in a flight). Ground work up to taxi is verified with the stand-in.
- What triggers the EXTERNAL_SYSTEM_SET/TOGGLE events BATC receives (probably key bindings inside MSFS).

## Next
1. Run `xplane-check` on Ben's PC with X-Plane at YBBN and fix any mapping it flags.
2. First BATC flight on X-Plane: `serve --facilities <MSFS recording>` at YBBN, taxi and fly to YTWB.
3. Phase 2: airport data from X-Plane's `apt.dat` and CIFP instead of the replay.
