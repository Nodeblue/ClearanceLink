# ADR-0001: Emulate the SimConnect server

**Status:** accepted (pending Phase 0 confirmation). **Date:** 2026-09-30.

## Context
The ATC app talks to MSFS through the app's helper program, which uses SimConnect. We could intercept at three points:
1. Be the SimConnect server on the MSFS pipe/TCP.
2. Replace the WebSocket protocol between the ATC app's Unity front end and its helper program.
3. Patch or replace the ATC app's binaries.

## Decision
Option 1. The SimConnect wire protocol is public and stable, with independent client implementations (node-simconnect, jSimConnect). It needs no change to the ATC app's files. Requests are self-describing (SimVar names travel as strings), which makes recording and mapping straightforward.

## Consequences
We must answer the Facilities API from X-Plane data, which is the largest piece of work. If Phase 0 shows the ATC app refuses to work without MSFS present, the fallback is option 2.
