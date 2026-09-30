# Status

_Update this file in every PR that changes behaviour._

**Phase:** 0, feasibility spike.

## Works
- SimConnect packet framing, function and message names, and decoding of the common ATC-client calls (`src/XPConnect.Protocol`).
- `XPConnect record`: pass-through proxy that records a real client/sim session to JSONL and writes a call inventory.
- `XPConnect fake`: answers the Open handshake as MSFS 2020 or 2024 and records every request. Nothing else is answered yet.
- `XPConnect summarize`: builds the call inventory from a recording.
- Verified against an independent client (node-simconnect 4.2) over TCP, on both the SunRise and KittyHawk protocols.
- Recorded a real BATC session against MSFS 2024 (YBBN to YTWB, 2026-09-30). BATC is redirected by a `SimConnect.cfg` next to `simconnect_ws.exe` and another next to `BeyondATC.exe`. Findings: `docs/research/batc-call-inventory.md`.
- `ServerIdentity.Msfs2024` matches the recorded Open reply (SunRise 12.2.282174.999, SimConnect 12.2.0.0).
- Recordings sample high-rate streams (AI position writes, SimObjectData) to one per second per stream by default (`--throttle-ms`). A full-rate 30-minute flight was 2.9 GB.

## Not yet verified
- BATC's `simconnect_ws.exe` connecting to our named pipe on Windows (message-mode pipe, security).
- Whether BATC needs MSFS running or installed beyond the SimConnect connection.
- BATC's behaviour with MSFS closed and `fake` standing in (runbook step 3).
- What triggers the EXTERNAL_SYSTEM_SET/TOGGLE events BATC receives (probably key bindings inside MSFS).

## Next
Runbook step 3 (stand-in test with MSFS closed), then Phase 1: answer the user-aircraft SimVar blocks and COM events from X-Plane.
