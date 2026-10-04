# ADR-0009: The app is called ClearanceLink

Date: 2026-10-04. Status: accepted.

## Context
The project started as XPConnect. Before the first public release Ben chose a name that says what it does for
simmers: **ClearanceLink - Connect BeyondATC to X-Plane 12**, by Nodeblue. He chose to rename everything at once
(user-facing text, files, projects and namespaces), not only what users see, so the code and the app never
disagree.

## Decision
- Everything named XPConnect is now ClearanceLink: `ClearanceLink.exe`, the release zip, the window, the docs, the
  `ClearanceLink.*` projects and namespaces, the X-Plane plugin (folder `Resources\plugins\ClearanceLink`, signature
  `com.nodeblue.clearancelink`, version 0.5.0), environment variables (`CLEARANCELINK_*`) and converted FSLTL models
  (`Custom Data\CSL\ClearanceLink-FSLTL`).
- Unchanged: the stand-in `FlightSimulator2024.exe` (BeyondATC looks for that name), the plugin's UDP protocol and
  ports, and the GitHub repository address, which Ben renamed to Nodeblue/ClearanceLink the same day (GitHub redirects the old one).
- An upgrade keeps everything. The old names are listed once in `OldNames`:
  - `%LOCALAPPDATA%\XPConnect` (settings, logs) is moved to `%LOCALAPPDATA%\ClearanceLink` on first start; if an old
    copy still has its log open, only settings.json is copied.
  - The old plugin folder counts as outdated on SETUP. Installing copies `csl_paths.txt`, `settings.txt` and any
    `Resources\CSL` across, then deletes the old folder so X-Plane doesn't load two plugins.
  - FSLTL models converted into `XPConnect-FSLTL` are used from there until the next CONVERT or UPDATE (X-Plane is
    closed then), which renames the folder first. Moving it while X-Plane runs could leave models it has loaded
    pointing at a folder that's gone.
  - A SimConnect.cfg backup made under the old name is still restored when ALWAYS ON is switched off.

## Alternatives
- **Rename only what users see.** Smaller, but every log line, folder and namespace would keep saying XPConnect, and
  support files would mix both names.
- **No migration (it's the first public release).** Ben already has settings, a plugin and 13 GB of converted FSLTL
  models under the old names, and X-Plane would load both plugins.
