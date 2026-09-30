# ADR-0003: Build airport answers from X-Plane's own files, on request

Date: 2026-09-30. Status: accepted.

## Context
BATC asks for every airport it needs (departure, destination, and every few seconds a surrounding airport) with
`RequestFacilityData`, and expects MSFS's answer: runways, parking, a taxi network, frequencies and procedures
(`docs/protocol/facility-data.md`). Phase 1 replayed MSFS's answers from a recording, which only covers airports
flown in MSFS and shows MSFS's scenery rather than what the pilot sees in X-Plane.

## Decision
Build the answer when BATC asks, from the X-Plane installation the pilot is flying:
- `apt.dat` in X-Plane's own scenery priority (`Custom Scenery/scenery_packs.ini`, then Global Airports), so gates
  and taxiways match the loaded scenery (for example Orbx YBBN rather than the Gateway YBBN).
- Procedures from `CIFP/<ICAO>.dat`, fix positions from `earth_fix.dat`/`earth_nav.dat`, each from `Custom Data`
  (a navdata update) before X-Plane's default data. Magnetic variation from the nearest VOR.
- Where X-Plane has no taxi network (many Gateway airports, YTWB among them), generate a minimal one: runway paths,
  a hold-short point beside each runway end, and one apron point joining them to every parking spot.
- A recorded MSFS answer (`--facilities`) still wins when given, for comparison and as a fallback.

The apt.dat files are indexed once in the background at start-up (about 1-2 s for Global Airports) and each airport
is built once and cached.

## Alternatives
- **Replay MSFS data only.** Exact for recorded airports, useless elsewhere, and wrong for X-Plane scenery.
- **A prebuilt database shipped with XPConnect.** Faster per request but goes stale with each navdata cycle and
  scenery install, and CIFP/Gateway licensing makes redistribution awkward.
- **Navigraph or other online APIs.** Needs accounts and network; X-Plane already has the data locally.

## Consequences
- Generated taxi networks are much sparser than MSFS's and carry no taxiway names at airports X-Plane has no
  network for; BATC's taxi instructions there are simple.
- Enum and unit conventions are reverse-engineered from MSFS's answers; mistakes show up as odd BATC behaviour and
  are fixed in `XPConnect.Airports` with a test.
