# ADR-0014: Let BeyondATC find FSLTL when MSFS 2024 isn't installed

Date: 2026-10-06. Status: proposed (Ben to confirm on the draft PR).

## Context
BeyondATC creates traffic only when it finds FSLTL, AIG or FS Traffic in MSFS 2024's Community folder, through MSFS
2024's UserCfg.opt (`docs/research/batc-traffic-models.md`). A tester with FSLTL converted by ClearanceLink got "No
traffic liveries found" and no traffic: their FSLTL is not in an MSFS 2024 Community folder (MSFS 2020, or a folder
chosen with CHOOSE).

## Decision
While `serve` runs (the window or `start`), `BatcTrafficModels`:
- does nothing when MSFS 2024 has its own UserCfg.opt (Store or Steam). If its Community folder has no traffic
  package, the log says so (`batc-models: ... only in MSFS 2024's ...`). MSFS's own files are never changed.
- otherwise, when ClearanceLink knows an FSLTL folder, writes `%APPDATA%\Microsoft Flight Simulator 2024\UserCfg.opt`
  (the Steam place) with a ClearanceLink marker line and `InstalledPackagesPath` naming:
  - the folder above `Community` when FSLTL sits in a Community folder (MSFS 2020), or
  - `%LOCALAPPDATA%\ClearanceLink\BeyondATC packages`, whose `Community\fsltl-traffic-base` is a directory junction to
    the chosen FSLTL folder (a junction needs no administrator rights or developer mode).
- removes the file (and its folder, if ClearanceLink made it and it is left empty) when serve stops, and while a real
  MSFS runs, so an MSFS 2024 being installed and started never adopts ClearanceLink's InstalledPackagesPath. The file
  names the ClearanceLink process that wrote it; another ClearanceLink leaves a running one's file alone, and a file
  whose ClearanceLink has gone is removed. A stale file also goes once MSFS 2024 has its own UserCfg.opt.
- refuses an FSLTL folder on a network share or with `%` or `"` in its path (a junction can't point at a share, and
  `cmd` would expand `%`), with a log line saying so.
- the `batc-guard` process removes it after a crash, in either connection mode. The junction stays; it is only a link.
- ClearanceLink's own FSLTL lookup and its NEEDS MSFS check ignore the marked file.

## Alternatives
- **Tell users to install FSLTL into MSFS 2024.** Not possible without MSFS 2024, and FSLTL's installer works per sim.
- **Answer as MSFS 2020 when only MSFS 2020 is installed.** Needs a different stand-in exe and handshake, all
  recorded only for MSFS 2024; far bigger and untested.
- **Link FSLTL into MSFS 2024's own Community folder** when MSFS 2024 has no FSLTL. MSFS 2024 would load that
  package too if it started; left out, the log explains instead.
- **Keep the file after ClearanceLink closes.** A later MSFS 2024 (Steam) install could pick up our
  InstalledPackagesPath. Removing it on close keeps the trick to the time ClearanceLink is open, like ADR-0012.

## Consequences
- With no MSFS 2024, other apps that look for `%APPDATA%\Microsoft Flight Simulator 2024\UserCfg.opt` may think MSFS
  2024 (Steam) is installed while ClearanceLink is open.
- BeyondATC still needs the FSLTL MSFS package on the PC; Bluebell alone gives no BeyondATC traffic.
