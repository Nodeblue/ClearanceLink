# XPConnect

Middleware that lets [BeyondATC](https://www.beyondatc.net/) (built for MSFS) work with X-Plane 12.

```
BeyondATC  <-- SimConnect -->  XPConnect  <-- Web API / files -->  X-Plane 12
```

XPConnect pretends to be MSFS on the SimConnect pipe. It turns each request into X-Plane datarefs, commands and navdata.

**Status:** BeyondATC flies with X-Plane 12 end to end: clearance, taxi routes from X-Plane's taxiways, SIDs and STARs, handoffs, approaches, COM tuning (YBBN, YTWB and YSSY tested). Not yet: AI traffic, in-sim push-to-talk and quick buttons. See [docs/STATUS.md](docs/STATUS.md).

## Using it (Windows)

Download `XPConnect-<version>-win-x64.zip`, unzip it anywhere and read `README.txt`. In short:
start X-Plane 12, double-click `XPConnect.exe` (the first time it offers to point BeyondATC at it), then start
BeyondATC. `Use BeyondATC with MSFS.cmd` switches BeyondATC back to MSFS. No .NET install is needed.

## Building from source

```powershell
winget install Microsoft.DotNet.SDK.10
./scripts/test.ps1                     # build + all tests
./scripts/run.ps1 serve                # run from source (X-Plane 12 running, BeyondATC pointed at XPConnect)
./scripts/run.ps1 batc status          # is BeyondATC pointed at XPConnect or MSFS? (batc on / batc off)
./scripts/publish.ps1                  # publish/XPConnect-<version>-win-x64.zip
```

Developer runbooks: [Phase 0 recording against MSFS](docs/runbooks/phase0.md), [X-Plane](docs/runbooks/phase1-xplane.md).
Instructions for coding agents: [AGENTS.md](AGENTS.md).