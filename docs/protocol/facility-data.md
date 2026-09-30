# Facility data as BATC requests it and MSFS 2024 answers it

Source: pass-through recordings on Ben's PC, 2026-09-30 (`20260930-165631-record.jsonl`, `20260930-174605-record.jsonl`), BATC 1.8.21.EA against MSFS 2024 1.8.16.0. Fixture: `recordings/samples/batc-1.8.21-facility-ytwb.jsonl` (the 573 `AddToFacilityDefinition` calls, the YTWB request and MSFS's 583-packet reply; one string tail of leftover memory bytes zeroed).

Everything below is read off the recordings. Sizes were checked by adding up the fields in the order BATC defined them.

## Request side
- `simconnect_ws` builds two definitions once per session, right after it first receives aircraft data: define **444** (318 fields, everything) and define **333** (255 fields: the same without `TAXI_PARKING`, `TAXI_NAME`, `TAXI_PATH` and `TAXI_POINT`).
- `RequestFacilityData(defineId, requestId, icao, region="")`. 444 is used for the departure and destination airports, 333 for surrounding airports (one request about every 3 s). Request ids count up from 1.
- An airport MSFS does not have is answered with `Exception` 1, index 3, carrying the request's send id. BATC retries it once a minute.

## Reply side
One `FacilityData` packet per object, then `FacilityDataEnd(requestId)`.

`FacilityData` header (28 bytes after the 12-byte packet header): `userRequestId`, `uniqueRequestId`, `parentUniqueRequestId`, `type`, `isListItem`, `itemIndex`, `listSize`, then the data block.

- Objects are sent **depth-first in the order the definition lists them**: an object, then each of its child lists in definition order, each child followed by its own children.
- `uniqueRequestId` is a counter that increases by one per packet and **carries on across requests on the connection** (YBBN used 0 to 8609, YTWB started at 8610). The airport's `parentUniqueRequestId` is 0.
- List children have `isListItem` 1, `itemIndex` 0..n-1 and `listSize` n. Single children (the six runway pavement blocks) have `isListItem` 0 and `itemIndex` = `listSize` = 0xFFFFFFFF, as does the airport itself.
- An empty list sends nothing.
- The data block holds exactly the fields named in the definition for that object, in definition order, packed with no padding.

YTWB with define 444, as a run-length sequence of `type x count`:
`0x1, 1x1, 23x6, 1x1, 23x6, 15x36, 17x7, 16x270, 3x4, 14x215, 2x4, 5x1 (6x1 7x2)x3 8x3 9x3, 5x1 (6x1 7x2)x3 8x3 9x3`

### Types seen and their data blocks (with BATC's definitions)

| Type | Object | Bytes | Fields in order |
|---|---|---|---|
| 0 | AIRPORT | 124 | LATITUDE f64, LONGITUDE f64, ALTITUDE f64 (metres), N_RUNWAYS i32, N_STARTS i32, N_TAXI_POINTS i32, N_TAXI_PATHS i32, NAME64 char[64], ICAO char[8], REGION char[8], MAGVAR f32 |
| 1 | RUNWAY | 68 | LATITUDE f64, LONGITUDE f64, ALTITUDE f64, HEADING f32, LENGTH f32, WIDTH f32, PATTERN_ALTITUDE f32, SURFACE i32, PRIMARY_NUMBER i32, PRIMARY_DESIGNATOR i32, SECONDARY_NUMBER i32, SECONDARY_DESIGNATOR i32, PRIMARY_ILS_ICAO char[8] |
| 23 | PAVEMENT (child of RUNWAY, six per runway: primary/secondary blastpad, threshold, overrun in definition order) | 12 | LENGTH f32, WIDTH f32, ENABLE i32 |
| 15 | TAXI_PARKING | 40 | TYPE i32, NAME i32, SUFFIX i32, NUMBER u32, ORIENTATION i32, HEADING f32, RADIUS f32, BIAS_X f32, BIAS_Z f32, N_AIRLINES i32 |
| 17 | TAXI_NAME | 32 | NAME char[32] |
| 16 | TAXI_PATH | 28 | TYPE i32, RUNWAY_NUMBER i32, RUNWAY_DESIGNATOR i32, START i32, END i32, NAME_INDEX i32, WIDTH f32 |
| 3 | FREQUENCY | 72 | TYPE i32, FREQUENCY i32 (Hz), NAME char[64] |
| 14 | TAXI_POINT | 16 | TYPE i32, ORIENTATION i32, BIAS_X f32, BIAS_Z f32 |
| 2 | START | 32 | LATITUDE f64, LONGITUDE f64, ALTITUDE f64, NUMBER i32, DESIGNATOR i32 |
| 5 | APPROACH | 28 | TYPE i32, SUFFIX i32, RUNWAY_NUMBER i32, RUNWAY_DESIGNATOR i32, N_TRANSITIONS i32, N_FINAL_APPROACH_LEGS i32, N_MISSED_APPROACH_LEGS i32 |
| 6 | APPROACH_TRANSITION | 16 | NAME char[8], TYPE i32, N_APPROACH_LEGS i32 |
| 7 / 8 / 9 | APPROACH_LEG / FINAL_APPROACH_LEG / MISSED_APPROACH_LEG | 84 | TYPE i32, FIX_ICAO char[8], FIX_REGION char[8], FIX_TYPE i32, APPROACH_ALT_DESC i32, ALTITUDE1 f32, ALTITUDE2 f32, SPEED_LIMIT f32, FIX_LATITUDE f64, FIX_LONGITUDE f64, COURSE f32, THETA f32, IS_IAF i32, IS_IF i32, IS_FAF i32, IS_MAP i32, APPROACH_SPEED_DESC i32 |
| 10 / 11 | DEPARTURE / ARRIVAL | 20 | NAME char[8], N_ENROUTE_TRANSITIONS i32, N_RUNWAY_TRANSITIONS i32, N_APPROACH_LEGS i32 |
| 12 | RUNWAY_TRANSITION | 12 | RUNWAY_NUMBER i32, RUNWAY_DESIGNATOR i32, N_APPROACH_LEGS i32 |
| 13 | ENROUTE_TRANSITION | 12 | NAME char[8], N_APPROACH_LEGS i32 |

- `AIRLINE` (child of TAXI_PARKING) is defined but was never sent: every parking spot recorded had N_AIRLINES 0.
- Type 7 legs appear under approach transitions (parent type 6), arrivals/departures (10, 11), runway transitions (12) and enroute transitions (13).
- MSFS leaves leftover memory after the terminator in some strings (seen in NAME64). Send zeros.
- BIAS_X / BIAS_Z of taxi points and parking are metres east / north of the airport reference point (the AIRPORT latitude/longitude). Not yet verified numerically; check against a parking spot with a known position before relying on it.

### Size of the job (MSFS data)
| Airport | Packets | Runways | Starts | Taxi points | Taxi paths | Parking | Taxi names | Frequencies |
|---|---|---|---|---|---|---|---|---|
| YBBN | 8,610 | 2 | 6 | 3,419 | 4,147 | 167 | 80 | 13 |
| YTWB | 583 | 2 | 4 | 215 | 270 | 36 | 7 | 4 |

## What X-Plane has for the same airports (Ben's install, X-Plane 12.4.3)

Fixtures: `tests/XPConnect.Airports.Tests/fixtures/YBBN.apt.dat` and `YTWB.apt.dat`, cut from `Global Scenery/Global Airports/Earth nav data/apt.dat` (Scenery Gateway data, 363 MB in full).

| Airport (source) | Runways (100) | Ramp starts (1300) | Taxi nodes (1201) | Taxi edges (1202) | Frequencies (105x) |
|---|---|---|---|---|---|
| YBBN (Global Airports) | 2 | 162 | 652 | 447 | 11 |
| YBBN (Orbx custom scenery, the one X-Plane actually loads here) | 2 | 182 | 2,239 | 1,139 | 8 |
| YTWB (Global Airports) | 2 (+1 helipad) | 72 | **0** | **0** | 2 |

Consequences for the generator:
1. **Scenery priority.** An airport can be defined in several scenery packs; X-Plane uses the first one in `Custom Scenery/scenery_packs.ini` (here `Orbx_A_YBBNv2_Brisbane` comes before `*GLOBAL_AIRPORTS*`). XPConnect must read the same one, or gates and taxiways will not match what the pilot sees. Payware apt.dat files must not be committed.
2. **Airports with no taxi network.** YTWB has no 1200/1201/1202 rows at all, only pavement and ramp starts. MSFS gave BATC 215 taxi points and 270 paths there. A fallback is needed (at least: connect each ramp start to the runways), otherwise BATC has nothing to route on.
3. **MSFS's graph is denser.** Its taxi points include parking connectors and hold-short points; X-Plane's 1202 edges carry the taxiway name and runway "hot zone" flags instead. Parking radius and type have to be derived from the 1300 row's aircraft classes and the 1301 width code.
4. **Procedures.** CIFP files are in `Custom Data/CIFP/<ICAO>.dat` when a navdata update is installed (here Navigraph AIRAC 2609), otherwise `Resources/default data/CIFP/`. They are licensed data: do not commit them; write test fixtures by hand.
5. `1302 transition_level` is written as `11000` in the Gateway file and `110` in the Orbx one.
