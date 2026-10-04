# ADR-0004: Draw BATC's traffic with our own XPMP2 plugin

Status: accepted (Ben, 2026-10-01)

## Context
BATC creates its AI traffic itself and moves every aircraft many times a second through SimConnect
(`docs/research/batc-call-inventory.md`); MSFS only draws them. It also asks the sim for the ground altitude under
each aircraft. X-Plane's Web API cannot create extra aircraft, so drawing them needs a plugin.

## Options
1. **LiveTraffic** fed over its RealTraffic network interface. No C++ for us, but users install and configure a
   second, complex plugin, and LiveTraffic buffers positions by several seconds to smooth internet data, so traffic
   would lag what BATC's controllers say (an aircraft still on the runway after a landing clearance).
2. **Our own plugin built on XPMP2** (MIT; the library LiveTraffic and xPilot use). Real time, can read X-Plane's
   terrain height for BATC, installed by ClearanceLink itself. C++, built by GitHub Actions so no toolchain on the
   user's PC.
3. X-Plane's built-in AI aircraft. About 20 slots of full flyable .acf aircraft with X-Plane's own AI attached.
   Not suitable.

## Decision
Option 2. `plugin/` holds the plugin; ClearanceLink talks to it over local UDP (`docs/protocol/plugin.md`), sends
type, airline and position, and gets terrain height back. The release zip carries the plugin and ClearanceLink.exe
installs it into `Resources/plugins/ClearanceLink`. Users install a CSL model package (Bluebell or one they already have
from LiveTraffic, xPilot or X-IvAp); none is shipped, as their licences do not allow redistribution.

## Consequences
- A second language and build (CMake, MSVC on GitHub Actions; `.github/workflows/plugin.yml`, `release.yml`).
  `scripts/publish.ps1` builds the zip without the plugin when CMake is missing.
- The plugin is the natural home for later X-Plane-side features such as push-to-talk commands.
