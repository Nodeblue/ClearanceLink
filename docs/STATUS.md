# Status

_Update this file in every PR that changes behaviour._

**Phase:** 0, feasibility spike.

## Works
- SimConnect packet framing, function and message names, and decoding of the common ATC-client calls (`src/XPConnect.Protocol`).
- `XPConnect record`: pass-through proxy that records a real client/sim session to JSONL and writes a call inventory.
- `XPConnect fake`: answers the Open handshake as MSFS 2020 or 2024 and serves the user aircraft's SimVars from a fixed snapshot (SF50 parked at YBBN, live clock, COM set events applied). `--facilities <recording.jsonl>` replays the airport data MSFS sent in a pass-through recording. Everything else is recorded and left unanswered.
- `src/XPConnect.SimStub` builds `FlightSimulator2024.exe`, the do-nothing process BATC needs to see before it connects. A console process with no window passes.
- `XPConnect summarize`: builds the call inventory from a recording.
- Verified against an independent client (node-simconnect 4.2) over TCP, on both the SunRise and KittyHawk protocols.
- Recorded a real BATC session against MSFS 2024 (YBBN to YTWB, 2026-09-30). BATC is redirected by a `SimConnect.cfg` next to `simconnect_ws.exe` and another next to `BeyondATC.exe`. Findings: `docs/research/batc-call-inventory.md`.
- `ServerIdentity.Msfs2024` matches the recorded Open reply (SunRise 12.2.282174.999, SimConnect 12.2.0.0).
- Stand-in test passed (2026-09-30): with MSFS closed, BATC connects both SimConnect connections to `fake --identity msfs2024` and accepts the handshake, provided a process named `FlightSimulator2024.exe` is running (a do-nothing stub passes). With user-aircraft data and replayed facility data served, BATC loads YBBN and the flight plan, gives IFR clearance, tunes COM1 through us and issues a taxi route, all without MSFS (2026-09-30).
- Recordings sample high-rate streams (AI position writes, SimObjectData) to one per second per stream by default (`--throttle-ms`). A full-rate 30-minute flight was 2.9 GB.

## Not yet verified
- BATC's `simconnect_ws.exe` connecting to our named pipe on Windows (message-mode pipe, security).
- BATC's behaviour once it receives user-aircraft data without MSFS (how far it gets, reconnects, any install or file checks later in a flight).
- What triggers the EXTERNAL_SYSTEM_SET/TOGGLE events BATC receives (probably key bindings inside MSFS).

## Next
Phase 1: ship a `FlightSimulator2024.exe` stub process, answer the user-aircraft SimVar blocks (defines 300 and 17) and the COM events, first with fixed values and then from X-Plane.
