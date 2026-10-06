# How BeyondATC finds its traffic models

Source: BeyondATC's own `Assembly-CSharp.dll` (`C:\BeyondATC\BeyondATC_Data\Managed`, file dated 2025-09-03), read as
IL on 2026-10-06, and Ben's `Player.log`. Prompted by a tester whose BeyondATC said "No traffic liveries found,
traffic will not be displayed. You must have at least one of FSLTL, AIG, FS Traffic or BravoATG models installed."
after converting FSLTL with ClearanceLink.

## The lookup
`TrafficAI.TrafficDataFeeder` calls `TrafficModelManager.MatchingInitialize(isSimConnectEnabled, PlayerData.simIs2024,
commonData)` once BeyondATC knows which sim it is on. ClearanceLink answers as MSFS 2024, so `simIs2024` is true.

1. `FindCommunity` picks one UserCfg.opt. For MSFS 2024: `%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\UserCfg.opt`
   (Microsoft Store) if it exists, else `%APPDATA%\Microsoft Flight Simulator 2024\UserCfg.opt` (Steam). (For MSFS
   2020 the same two places under `Microsoft.FlightSimulator_8wekyb3d8bbwe` and `Microsoft Flight Simulator`.)
   Neither exists: it returns "" and finds nothing. It reads `InstalledPackagesPath\s+"([^"]*)"` and returns
   `<path>\community\`. It never looks at MSFS 2020 when on MSFS 2024, nor anywhere else.
2. `GetFiles` lists the folders in that Community folder and keeps each whose lower-case name **contains**
   `fsltl-traffic-base`, `justflight-aircraft-traffic-fleet`, `justflight-fstraffic`, `aig-aitraffic-oci` or
   `gamod1-models`. It collects every `aircraft.cfg` under them (all subfolders). Errors (missing folder, broken link)
   are caught and give nothing.
3. `GenerateModels` reads each `aircraft.cfg` (its `[FLTSIM.n]` liveries) and the `flight_model.cfg` beside it
   (contact point 0, reference datum, static pitch, static CG height). The author (FSLTL, AIG, FS Traffic) comes from
   the path. On MSFS 2024 it then probes for BravoATG through SimConnect (`EnumerateSimObjectsAndLiveries`, looking
   for Bravo-only livery names).
4. `InitializeContinueAfterModelProbe`: when FSLTL + AIG + FS Traffic + BravoATG liveries add up to 0, it logs
   `[Traffic] No traffic liveries found, traffic will not be displayed.`, shows the warning and **loads no traffic at
   all**, not even traffic that only talks on frequency. Otherwise it logs
   `[Traffic] Available liveries <n> FSLTL, <n> AIG, <n> FS Traffic and <n> BravoATG` (Ben's PC: 2381 FSLTL).

The log line `[CommunityFolder] <path>` shows the folder it used.

## What this means for ClearanceLink
- BeyondATC needs FSLTL (or AIG / FS Traffic) installed **as an MSFS package**, in MSFS 2024's Community folder, to
  create any traffic. ClearanceLink's converted X-Plane models and Bluebell are invisible to it; they only decide how
  the traffic BeyondATC creates is drawn.
- A PC without MSFS 2024 (MSFS 2020 only, or FSLTL kept in another folder) gets no traffic. ClearanceLink fixes that
  while it is open (ADR-0014, `BatcTrafficModels`).
- A PC with MSFS 2024 whose Community folder has no FSLTL also gets no traffic. ClearanceLink leaves MSFS 2024's files
  alone and says so in its log.
