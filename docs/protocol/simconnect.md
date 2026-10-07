# SimConnect wire protocol notes

Facts here must cite a source: a recording in `recordings/`, the MSFS SDK docs, or a reference implementation (node-simconnect `src/SimConnectSocket.ts`, `src/SimConnectPacketBuilder.ts`).

## Framing (node-simconnect, verified against it in tests)
All little-endian.
- Client to server: `u32 size | u32 protocol | u32 0xF0000000|functionId | u32 sendId | payload`
- Server to client: `u32 size | u32 protocol | u32 recvId | payload`
- `size` includes the header.
- Protocol versions: 2 FSX RTM, 3 FSX SP1, 4 FSX SP2, 5 KittyHawk (MSFS 2020), 6 SunRise (MSFS 2024).

## Transports
- Default pipe: `\\.\pipe\Microsoft Flight Simulator\SimConnect`. A client without `SimConnect.cfg` connects there.
- TCP: configured in the sim's `SimConnect.xml`. Clients find it through `SimConnect.cfg` (`[SimConnect] Protocol=IPv4 Address=127.0.0.1 Port=NNNN`).

## Open handshake
Client payload: `char[256] appName | u32 0 | u8 0 | char[3] alias ("HK" KittyHawk, "RS" SunRise, "XSF" FSX) | u32 major | u32 minor | u32 buildMajor | u32 buildMinor`.
Server reply `RecvId.Open (2)`: `char[256] applicationName | u32 x 8 versions (app then SimConnect) | u32 reserved1 | u32 reserved2`.
Recorded MSFS 2024 reply (2026-09-30, protocol 6): `"SunRise"`, app 12.2.282174.999, SimConnect 12.2.0.0. BATC's simconnect_ws logs `app='SunRise' version=12.2 -> msfs2024=true`, so it identifies the sim from this reply alone.

## BATC 1.8.21 connections (recording 2026-09-30)
- `BeyondATC` (simconnect_ws.exe) reads a 27-SimVar user-aircraft block at about 1 Hz, plus all Facilities data.
- `BATCDLLSimConnection` (BeyondATC.exe) reads a 35-SimVar block at about 580 Hz, sends COM_RADIO_SET_HZ / COM_STBY_RADIO_SET_HZ, writes GSX L-vars, and creates and drives AI aircraft (AICreateSimulatedObject, AIReleaseControl, SetDataOnSimObject).
- Both connections honour a `SimConnect.cfg` in the executable's folder.
- An unknown airport in RequestFacilityData gets exception 1, and BATC retries every minute.

## BATC 1.8.22 connections (recording 20261007-115756-serve on Ben's PC, excerpt in recordings/samples/batc-1.8.22-setup.jsonl, BATC Player.log and Assembly-CSharp.dll, 2026-10-07)
1.8.22 has no simconnect_ws.exe. BeyondATC.exe loads `BeyondATC_Data\Plugins\x86_64\SimConnect.dll` and opens three connections, all redirected by the `SimConnect.cfg` next to BeyondATC.exe:
- `BATCDLLSimConnection`: as in 1.8.21 (aircraft, radios, events, AI traffic, EnumerateSimObjectsAndLiveries).
- `BATC_Facilities`: AddToFacilityDefinition and RequestFacilityData for the player's airports and traffic airports (Player.log `[SimConnectBridge] <ICAO> player|traffic facility data ready`). It took over the facility work of the old `BeyondATC` connection.
- `BATC_TrafficSync`: two data definitions of the user aircraft (lat, lon, altitude, bank, pitch, true heading, IAS; and lat, lon, true heading, IAS, AGL, pitch) and `SubscribeToSystemEvent "Frame"`. BATC's `FrameSyncedInjector` is active only while Frame events arrive (last one under 0.5 s ago); otherwise "legacy per-frame injection stays in use" (SetDataOnSimObject on `BATCDLLSimConnection`, as 1.8.21).
- The sim generation comes from the Open reply (`[SimConnectBridge] SimConnect OPEN: app='SunRise' ... -> Msfs2024`); the FlightSimulator2024 process scan is now only a first guess ("No Flight Simulator process visible ... relying on SimConnect").
- Flight start waits on `CAMERA STATE` ("[FlightLoad] Holding until the player is in the flight"); an unknown value is accepted after 60 s. With 2, Player.log shows `[Sim] CAMERA STATE 0 -> 2 | InFlight`.
- `BATC_Facilities` and `BATC_TrafficSync` stayed open for the whole flight (no disconnect in the serve log after they reopened).
- Never send the clock as zero. In a tester's support zip (issue #36, 2026-10-07) ClearanceLink 0.7.5's first define 17 answer had ZULU TIME, ZULU DAY OF YEAR, LOCAL TIME and ABSOLUTE TIME at 0 (X-Plane had sent the position but not the time yet). Right after `[Sim] CAMERA STATE 0 -> 2`, BATC logged `OverflowException: Negating the minimum value of a twos complement number is invalid. at UI.MainInterface.ShouldWarnLiveTrafficTimeMismatch ()` from `PanelSequence`, and the load stayed at 25%. From 0.7.7 a block with a clock SimVar is held until X-Plane has sent its time datarefs.
- At start BATC may open `BATC_Facilities` and `BATC_TrafficSync`, drop them at once, and open all three again about 17 s later (both times in the same recording).

## Function ids
See `src/ClearanceLink.Protocol/FunctionNames.cs` (0x01 Open ... 0x6C CallCommBusEvent). 0x02 and 0x03 are not identified yet.

## AI traffic (BATC 1.8.21 against MSFS 2024, recording 20260930-174605; fixture recordings/samples/batc-1.8.21-traffic.jsonl)

- `EnumerateSimObjectsAndLiveries` (0x5B): `requestId`, `type` (BATC sends type 2). MSFS answers with
  `EnumerateSimObjectAndLiveryList` (recv 38): list template `requestId, arraySize, entryNumber, outOf`, then
  `arraySize` entries of `title[256], livery[256]` (512 bytes). 79 entries per packet, 70 packets for 5,497 entries.
  BATC waits for this list before creating traffic; it also scans FSLTL liveries from the MSFS Community folder itself
  (`Player.log`: "Available liveries 2381 FSLTL") and gives the BravoATG part of its list 15 s.
- Creation: `AICreateNonATCAircraft` (container title, tail number, 56-byte `SIMCONNECT_DATA_INITPOSITION`, requestId),
  `_EX1` (adds a livery after the title) for "Asobo PassiveAircraft ..." models, and `AICreateSimulatedObject` for
  pushback tugs ("ASO_Pushback_White"). MSFS answers `AssignedObjectId (requestId, objectId)` within ~15 ms; the ids
  are opaque (e.g. 207716368).
- Then `AIReleaseControl`, events `FREEZE_LATITUDE_LONGITUDE_SET` / `FREEZE_ATTITUDE_SET` (data 1), and
  `SetDataOnSimObject` many times a second per object: define 4 (airborne: lat, lon, altitude ft, bank, pitch,
  heading true, IAS kt), define 5 (on the ground: lat, lon, heading, IAS, height above ground ft, pitch), 6 (N1/N2,
  throttle), 0 (Initial Position), 1, 2, 12, 14 (animation state), plus light/flap/gear/spoiler events.
- BATC polls each aircraft with ONCE requests: define 18 (GROUND ALTITUDE ft, PLANE ALTITUDE ft, GROUND VELOCITY kt),
  19 (PLANE ALT ABOVE GROUND MINUS CG), 20 (PLANE ALTITUDE). For a frozen AI, MSFS's GROUND VELOCITY was about the IAS
  BATC wrote (254 kt for 249 kt at FL224), and GROUND ALTITUDE the terrain under it.
- `AIRemoveObject` when BATC is done with one; there is no reply.
