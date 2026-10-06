# BATC call inventory (Phase 0)

Source: `recordings/local/20260930-174605-record.jsonl` on Ben's PC (not committed, 2.9 GB). Pass-through recording between BeyondATC 1.8.21.EA (`simconnect_ws` 0.14.1.1) and MSFS 2024 1.8.16.0, YBBN to YTWB in the Cirrus Vision Jet (ATC MODEL `SF50`; the SimBrief plan was filed as a DA42), BATC traffic on, 2026-09-30 07:46Z to 08:16Z. 4,636,079 packets. The flight ended before landing and part of it was flown at increased sim rate. An earlier ground-only recording (`20260930-165631-record.jsonl`) agrees on connection 1.

## Go/no-go notes

- **Go.** With MSFS closed, BATC connected both SimConnect connections to `ClearanceLink fake --identity msfs2024` (see "Stand-in test" below).
- BATC opens **two** SimConnect connections. Both honour a `SimConnect.cfg` (one next to `simconnect_ws.exe`, one next to `BeyondATC.exe`), so both can be pointed at ClearanceLink over TCP without touching MSFS.
- Before connecting, BATC checks for a running process named `FlightSimulator2024.exe`; a do-nothing stub with that name passes. On the SimConnect side, sim detection is from the Open reply only: `simconnect_ws` logs `SimConnect OPEN: app='SunRise' version=12.2 -> msfs2024=true`. No system events are subscribed and no system states are requested.
- Everything BATC needs about the **user aircraft** is two data definitions plus two COM events. All of it has an obvious X-Plane source.
- **Facility data** is the large job: 80 requests for 66 airports, 34,271 replies.
- **AI traffic** is fully driven by BATC: it creates objects, releases them from sim AI control and writes their position and animation state continuously. This is 99% of the packet volume and needs an X-Plane plugin. It can be left out of the first milestone (run BATC with traffic off).
- Not yet known: PTT and button behaviour, and whether BATC reconnects after ClearanceLink restarts. Both need the fake to return data first.

## Handshake

| Conn | Client name | Open (client) | Open reply (MSFS 2024) |
|---|---|---|---|
| 1 | `BeyondATC` (`simconnect_ws.exe`) | alias `RS`, 12.2.0.0, protocol 6 | `SunRise` app 12.2.282174.999, SimConnect 12.2.0.0 |
| 2 | `BATCDLLSimConnection` (`BeyondATC.exe`) | alias `RS`, 12.2.0.0, protocol 6 | same |

`ServerIdentity.Msfs2024` uses these recorded values.

## Connection 1: simconnect_ws (user state + facilities)

| Call | Count |
|---|---|
| AddToDataDefinition | 27 |
| RequestDataOnSimObject | 1 (define 300, user, period 4 = once per second) |
| AddToFacilityDefinition | 573 (defines 444 and 333) |
| RequestFacilityData | 80 |

- Define 300 (user aircraft): PLANE LATITUDE, PLANE LONGITUDE, INDICATED ALTITUDE CALIBRATED, PRESSURE ALTITUDE, PLANE HEADING DEGREES TRUE/MAGNETIC, COM ACTIVE FREQUENCY:1/:2, TRANSPONDER CODE:1, AIRSPEED INDICATED, GROUND VELOCITY, DESIGN CRUISE ALT, ESTIMATED CRUISE SPEED, DESIGN SPEED CLIMB/VS0/VS1, DESIGN TAKEOFF SPEED, TYPICAL DESCENT RATE, WING SPAN, TIME OF DAY, ZULU TIME, ZULU DAY OF WEEK, CAMERA STATE, SIM ON GROUND, ATC HEAVY, ATC TYPE, ATC MODEL.
- Define 444 is the full airport definition (used for YBBN and YTWB); define 333 is used for the surrounding airports. Fields cover airport, runways (thresholds, overruns, blastpads), starts, frequencies, taxi points/paths/parking/names, airlines at parking, and approaches, departures and arrivals with transitions and legs. The full field list is in the recording's `.md` summary.
- YBBN took about 14 s to load from MSFS (`Player.log`: `[AirportLoad] YBBN load from sim took 14088ms`).
- 6 exceptions, all exception 1 for `RequestFacilityData` of `YSAM` (define 333), retried once a minute. BATC carries on, so an unknown airport can be answered with exception 1.

## Connection 2: BATCDLLSimConnection (events, traffic, GSX)

| Call | Count |
|---|---|
| AddToDataDefinition | 123 |
| MapClientEventToSimEvent | 39 |
| AddClientEventToNotificationGroup | 2 (group 1008: EXTERNAL_SYSTEM_SET, EXTERNAL_SYSTEM_TOGGLE) |
| RequestDataOnSimObject | 1,106,266 |
| SetDataOnSimObject | 2,374,726 |
| TransmitClientEvent | 9,625 |
| EnumerateSimObjectsAndLiveries | 1 (type 2; 70 reply packets) |
| AICreateNonATCAircraft / _EX1 / AICreateSimulatedObject | 49 / 8 / 7 |
| AIReleaseControl / AIRemoveObject | 64 / 60 |

### User aircraft
- Define 17 is **polled** with period 1 (once) about 580 times a second (1,071,487 requests): position, altitudes, headings, IAS, ground speed, GROUND ALTITUDE, COM ACTIVE FREQUENCY:1/:2, COM VOLUME:1/:2, COM STATUS:1/:2, COM RECEIVE:1/:2, COM TRANSMIT:1/:2, TRANSPONDER CODE:1, TRANSPONDER STATE:1, TRANSPONDER IDENT:1, ZULU TIME, ZULU DAY OF WEEK, ZULU DAY OF YEAR, LOCAL TIME, ABSOLUTE TIME, CAMERA STATE, SIM ON GROUND, WING SPAN, GENERAL ENG COMBUSTION:1/:2, AMBIENT WIND DIRECTION, AMBIENT WIND VELOCITY.
- Events sent to the user aircraft: only `COM_STBY_RADIO_SET_HZ` and `COM_RADIO_SET_HZ`, 9 each (for example data 127850000). `XPNDR_SET`, `XPNDR_IDENT_ON`, `COM1_RADIO_SWAP`, the COM2 events and `BAROMETRIC` are mapped but were not sent on this flight.
- Events received: 110 `EXTERNAL_SYSTEM_SET` / `EXTERNAL_SYSTEM_TOGGLE` with data 0 to 9. Ben reports pressing buttons during the flight for the map, the taxi map, tuning a frequency and the ATC quick replies, which fits these being BATC's button bindings arriving through MSFS (the data value selecting the action). The value-to-action table is not confirmed and needs a targeted recording (press each button once, in a known order).
- GSX: L-vars `L:FSDT_GSX_*` are defined; `PUSHBACK_STATUS` is subscribed once per second and `MENU_OPEN` / `MENU_CHOICE` were written 4 times. Optional.
- 1 exception: exception 7 for `MapClientEventToSimEvent` `THROTTLE_100`. BATC ignores it.

### AI traffic
- 64 objects created with FSLTL and Asobo passive models; each gets `AssignedObjectId`, then `AIReleaseControl` and `FREEZE_LATITUDE_LONGITUDE_SET` / `FREEZE_ALTITUDE_SET` / `FREEZE_ATTITUDE_SET`.
- BATC then writes each object continuously: define 4 and 5 (position, attitude, IAS), define 6 (N1/N2/throttle), define 0 (`Initial Position`), define 12, 14, 2, 1 (surfaces, gear, lights, engines), plus light, flap, gear and spoiler events (`SPOILERS_ON` 8,925 times).
- Lights (same recording, define 12 and the `*_LIGHTS_SET` / `STROBES_SET` events, checked 2026-10-06): `LIGHT NAV`, `LIGHT BEACON`, `LIGHT LANDING`, `LIGHT TAXI`, `LIGHT STROBE` are INT32 Bool in define 12, written for only 8 of the 57 objects (nav, beacon and strobe on; never landing or taxi). Every other object gets the light events instead: all five off when created (parked aircraft stay dark), then for example nav, beacon and strobes on at push, landing on for take-off, landing and strobes off and taxi on after landing. No object got both a SimVar write and an event. A ClearanceLink session (`20261003-210928-serve.jsonl`) shows the same.
- It reads back per object: define 18 (GROUND ALTITUDE, PLANE ALTITUDE, GROUND VELOCITY), define 19 (PLANE ALT ABOVE GROUND MINUS CG), define 20 (PLANE ALTITUDE). Ground altitude under each AI aircraft is therefore something the sim side must supply.

## Stand-in test (runbook step 3, MSFS closed)

Source: `recordings/local/20260930-183230-fake.jsonl` and BATC's `Player.log`, 2026-09-30 08:32Z to 09:01Z. `ClearanceLink fake --listen tcp:5112 --identity msfs2024`.

| Attempt | Result |
|---|---|
| No MSFS process | `Player.log`: `Detected Microsoft Flight Simulator 2020 is in use`, then `ERROR DIALOG: Flight Simulator is not running!`. `simconnect_ws.exe` is not launched and nothing connects. |
| Stub window titled `Microsoft Flight Simulator 2024 - 1.8.16.0`, process name `SimStub` | Same error. The window title alone does not pass. |
| Same stub, process name `FlightSimulator2024` | `Detected Microsoft Flight Simulator 2024 is in use`, `Flight Simulator Build Version: 1.0.0.0 \| module=FlightSimulator2024.exe size=0x2D000`. `simconnect_ws` starts and both connections open. |

- The stub was an empty WinForms program. BATC read its file version (1.0.0.0) and module size and carried on, so there is no check of signature, path or real version.
- `simconnect_ws` logged `SimConnect OPEN: app='SunRise' version=12.2 -> msfs2024=true` against the fake.
- BATC then sent its 150 data definitions and 39 event mappings and polled define 17 (user aircraft, period once) about 10 times a second: 13,540 requests in 25 minutes. With no `SimObjectData` reply its UI showed "Waiting for connection to Sim..." and then a connection timeout. It kept polling and did not disconnect.
- No facility requests were made in this state; they start once a flight is set up.
- `--identity msfs2020` was not tried.
- Consequence for ClearanceLink: it must run (or ship) a stub process named `FlightSimulator2024.exe`, and the first thing to implement is the `SimObjectData` reply for defines 300 and 17.

## Stand-in with data (MSFS closed, 2026-09-30 09:23Z)

Source: `recordings/local/20260930-191918-fake.jsonl`, `Player.log` and Ben's report. `fake --identity msfs2024 --facilities <facility packets from the two pass-through recordings>` plus the `FlightSimulator2024.exe` stub (console process, no window). BATC traffic set to 5/5/5/5.

- BATC reported connected, took YBBN from the served position and loaded it from the replay in 10.9 s (8,610 packets; MSFS took 14.1 s), then YTWB (583 packets) and 15 surrounding airports. No facility request went unanswered.
- The frequency list and the parking map displayed. The SimBrief plan loaded (departure 01R).
- IFR clearance worked, including readback. BATC tuned COM1 with `COM_STBY_RADIO_SET_HZ` + `COM_RADIO_SET_HZ`: 118.850 (Clearance), then 122.250 (Ground). The fake applied them and BATC showed the new frequency.
- Pushback/taxi request worked and produced a route on the map.
- Not tested: PTT and the quick-button bindings that arrive as `EXTERNAL_SYSTEM_*` events, anything needing the aircraft to move, reconnects.
- `EnumerateSimObjectsAndLiveries` (type 2) was sent once and not answered. No AI create calls followed, so BATC appears to wait for that list before injecting traffic. Nothing else was left unanswered.
- So for BATC to run a ground session it needs only: the process stub, the Open reply, `SimObjectData` for defines 300 and 17, the two COM events, and facility data.

## Operational notes from the session- After an MSFS crash, add-ons it launched (FSUIPC7, BravoLED, TDS GTNXi) kept the dead process's TCP port 500 listener alive; the restarted MSFS could not bind 500 and connections to it hung with no Open reply. Ending those processes, or forwarding to the new MSFS's dynamic IPv4 port, fixes it.
- The MSFS crash itself was in ChasePlane's `CP.dll` (0xc000001d), unrelated to the recorder.
- A 30-minute flight with traffic produces a 2.9 GB `.jsonl`. The recorder needs a way to drop or sample the high-rate AI `SetDataOnSimObject` / poll traffic.
