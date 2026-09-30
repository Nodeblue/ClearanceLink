# Status

_Update this file in every PR that changes behaviour._

**Phase:** 0, feasibility spike.

## Works
- SimConnect packet framing, function and message names, and decoding of the common ATC-client calls (`src/XPConnect.Protocol`).
- `XPConnect record`: pass-through proxy that records a real client/sim session to JSONL and writes a call inventory.
- `XPConnect fake`: answers the Open handshake as MSFS 2020 or 2024 and records every request. Nothing else is answered yet.
- `XPConnect summarize`: builds the call inventory from a recording.
- Verified against an independent client (node-simconnect 4.2) over TCP, on both the SunRise and KittyHawk protocols.

## Not yet verified
- BATC's `simconnect_ws.exe` connecting to our named pipe on Windows (message-mode pipe, security).
- Whether BATC needs MSFS running or installed beyond the SimConnect connection.
- The real `Open` reply values from MSFS 2024. `ServerIdentity` holds placeholders.

## Next
Run [runbooks/phase0.md](runbooks/phase0.md) on Ben's PC and commit the call inventory to `docs/research/batc-call-inventory.md`.
