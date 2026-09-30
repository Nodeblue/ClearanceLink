# ADR-0001: Emulate the SimConnect server

**Status:** accepted (pending Phase 0 confirmation). **Date:** 2026-09-30.

## Context
BATC talks to MSFS through `simconnect_ws.exe`, which uses SimConnect. We could intercept at three points:
1. Be the SimConnect server on the MSFS pipe/TCP.
2. Replace the WebSocket protocol between BATC's Unity app and `simconnect_ws.exe`.
3. Patch or replace BATC binaries.

## Decision
Option 1. The SimConnect wire protocol is public and stable, with independent client implementations (node-simconnect, jSimConnect). It needs no change to BATC's files. Requests are self-describing (SimVar names travel as strings), which makes recording and mapping straightforward.

## Consequences
We must answer the Facilities API from X-Plane data, which is the largest piece of work. If Phase 0 shows BATC refuses to work without MSFS present, the fallback is option 2.
