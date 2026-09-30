# XPConnect

Middleware that lets [BeyondATC](https://www.beyondatc.net/) (built for MSFS) work with X-Plane 12.

```
BeyondATC  <-- SimConnect -->  XPConnect  <-- Web API / files -->  X-Plane 12
```

XPConnect pretends to be MSFS on the SimConnect pipe. It turns each request into X-Plane datarefs, commands and navdata.

**Status:** Phase 0 (feasibility spike). See [docs/STATUS.md](docs/STATUS.md) and the [plan](docs/research/2026-09-30-initial-plan.md).

## Quick start (Windows)

```powershell
winget install Microsoft.DotNet.SDK.10
./scripts/test.ps1                     # build + all tests
./scripts/run.ps1 fake                 # pretend to be MSFS 2024 on the default pipe (MSFS must be closed)
./scripts/run.ps1 summarize recordings/local/<file>.jsonl
```

To record a real BeyondATC session against MSFS, see [docs/runbooks/phase0.md](docs/runbooks/phase0.md).
