# Phase 0 runbook: go/no-go on Ben's PC

Goal: learn exactly what the ATC app asks the simulator for, and whether it works when ClearanceLink stands in for MSFS.
Output: `docs/research/atc-app-call-inventory.md`, with the go/no-go findings at the top.

## 0. Setup
```powershell
winget install Microsoft.DotNet.SDK.10
git clone https://github.com/Nodeblue/ClearanceLink; cd ClearanceLink
./scripts/test.ps1
```

## 1. Find out how the ATC app connects
- List the app's install folder and look for `SimConnect.dll` or `SimConnect.cfg` next to the app's helper program.
- Find MSFS 2024's `SimConnect.xml`. For the MS Store build it is `%LOCALAPPDATA%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\SimConnect.xml`; for Steam it is `%APPDATA%\Microsoft Flight Simulator 2024\SimConnect.xml`. Note the pipe and TCP entries.
- **Back up any file before editing it, and restore it afterwards.**

## 2. Record a real session (MSFS 2024 running)
Put ClearanceLink between the ATC app and MSFS. Pick one:
- **A. Move MSFS's pipe.** In `SimConnect.xml`, rename MSFS's pipe (for example to `...\SimConnectReal`) and restart MSFS. Then run `./scripts/run.ps1 record --listen pipe --forward "pipe:Microsoft Flight Simulator\SimConnectReal"`.
- **B. Point the ATC app at TCP.** Make sure MSFS has an IPv4 entry (for example port 5111). Put a `SimConnect.cfg` next to the app's helper program (`[SimConnect]`, `Protocol=IPv4`, `Address=127.0.0.1`, `Port=5112`). Then run `./scripts/run.ps1 record --listen tcp:5112 --forward tcp:5111`. This only works if the app's helper program uses the native SimConnect.dll.

Then fly a short gate-to-gate flight with the ATC app: SimBrief plan loaded, clearance, taxi, takeoff, a handoff, approach, landing, taxi in. Traffic can be on for the first run to capture the AI calls. Stop with Ctrl+C; the `.md` summary is written next to the `.jsonl`.

## 3. Stand in for MSFS (MSFS closed)
```powershell
./scripts/run.ps1 fake --identity msfs2024
```
Start the ATC app. Record whether it connects, what it asks for, and what its UI and `Player.log` say. Repeat with `--identity msfs2020`. If the real Open reply from step 2 differs from `ServerIdentity`, update the placeholders first.

## 4. Write it up
- Copy the step 2 summary into `docs/research/atc-app-call-inventory.md`, with go/no-go notes at the top: does it connect without MSFS, are there process or file checks, PTT behaviour, and whether the app's helper program reconnects after ClearanceLink restarts.
- Commit a sanitised excerpt of the recording (handshake and first few seconds) to `recordings/samples/` as a test fixture.
- Update `docs/STATUS.md`.
