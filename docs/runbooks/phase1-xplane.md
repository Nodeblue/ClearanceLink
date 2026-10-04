# Runbook: BATC on X-Plane 12 (Phase 1)

Runs on Ben's PC (Windows) with MSFS **closed**.

## One-time setup
1. X-Plane 12.1.4 or later. Web API on: Settings > Network > "Enable incoming traffic" / Web API port 8086 (on by default; it only listens on localhost).
2. BATC redirected to ClearanceLink: `SimConnect.cfg` (`Protocol=IPv4`, `Address=127.0.0.1`, `Port=5112`) in both
   `C:\BeyondATC\BeyondATC_Data\StreamingAssets\` and `C:\BeyondATC\`. Remove both to fly MSFS normally again.
3. X-Plane's own ATC muted: set the ATC volume to 0 in X-Plane's sound settings, or its ATIS and controllers talk over BATC.
4. BATC traffic off for now (AI traffic needs the X-Plane plugin, later).

## Check the mapping (X-Plane running, any aircraft loaded)
```powershell
./scripts/run.ps1 xplane-check
```
Every line should read `Ok`. `NOT FOUND` means a dataref name in `mapping/simvars.yaml` is wrong for this X-Plane
version; lines marked `VERIFY` need their values compared with the cockpit (units, sign). Fix the YAML, not the code.

## Fly
1. Start X-Plane 12 and load the aircraft at a gate at YBBN.
2. Start ClearanceLink:
   ```powershell
   ./scripts/run.ps1 serve
   ```
   It starts the `FlightSimulator2024` stub itself, logs `xplane CONNECTED to X-Plane 12.x` and
   `airports: indexed N airports in M apt.dat files`. Airports come from the X-Plane folder X-Plane's installer lists
   (`%LOCALAPPDATA%\x-plane_install_12.txt`); pass `--xplane-root "C:\X-Plane 12"` if it picks the wrong one.
   Add `--facilities recordings\local\<MSFS recording>.jsonl` to answer recorded airports with MSFS's data instead.
3. Start BATC. Expect "connected", YBBN loaded, and BATC's map showing the aircraft where X-Plane has it.
   ClearanceLink logs one `airports: YBBN from <apt.dat>: ...` line per airport it builds.

## Check an airport without BATC
```powershell
./scripts/run.ps1 airport YBBN --out recordings\local\YBBN.xplane.json
```
Writes what ClearanceLink would send BATC for that airport, and logs which apt.dat and CIFP file it came from.

## When something is wrong
- ClearanceLink's console: `UNMAPPED SimVar`, `NOT FOUND dataref`, `holding aircraft data` (X-Plane not connected or no position),
  and on disconnect a per-client summary of unmapped and waiting SimVars.
- The recording in `recordings/local/*-serve.jsonl` and its `.md` summary.
- BATC's `Player.log` (`%USERPROFILE%\AppData\LocalLow\Skirmish Mode Games, Inc\BeyondATC\Player.log`).
