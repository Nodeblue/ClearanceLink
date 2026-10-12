# ADR-0015: A generic SimConnect bridge; the user chooses the ATC app

Date: 2026-10-12. Status: accepted (0.7.7).

## Context
ClearanceLink was built and tested against one MSFS-only ATC add-on, and found that app by itself (its uninstall
entry, its default folder). The developers of that app don't support running it outside MSFS, and ClearanceLink must
not look like it is made for, or endorsed by, any one app. What it really does is general: it stands in for MSFS as a
SimConnect server and answers from X-Plane 12. So it becomes an experimental SimConnect to X-Plane 12 bridge, a
development proof of concept, and the user says which app to point at it.

## Decision
- The user picks the ATC app's `.exe`: on the FOLDERS welcome page or on SETUP (row ATC APP, CHOOSE opens a file
  picker), or with `settings app "<path to the .exe>"` (`settings app none` clears it). Nothing is searched for. Until
  an app is chosen, STATUS's bottom line says CHOOSE ATC APP: SETUP.
- Everything else is worked out from that .exe:
  - its folder, where `SimConnect.cfg` goes (and `<Name>_Data\StreamingAssets` when the app has that folder, for app
    versions that run a helper program from there);
  - for a Unity app, `<Name>_Data\app.info` gives the company and product, so the log is
    `%USERPROFILE%\AppData\LocalLow\<company>\<product>\Player.log` (pushback approvals for the PUSH page, the
    support zip's `app/` folder) and its saved settings are Unity PlayerPrefs under `HKCU\Software\<company>\<product>`
    (the SimBrief pilot ID).
- Settings carry over: a user of an earlier version who had set the app's folder by hand gets the .exe found in that
  folder, and an install an earlier version found by itself is looked for once in the same old places, so upgraded
  users don't pick again. New users pick it once.
- The client connection names recorded from one app stay in the code as recorded wire facts, so the MSFS stand-in
  still closes at the right time for that app (ADR-0010). Any other client counts as connected as soon as it opens a
  connection, but only while serve listens on the app's own TCP port (the redirect); on the fallback, which also
  answers MSFS's default pipe, other MSFS add-ons connect too, so only the recorded names count there.
- Neutral names everywhere a user looks: screens (ATC APP, CONNECTIONS, START ATC APP, APP: X-PLANE TILL EXIT...),
  commands (`app on|off|status`, `app-guard`, `--app`, `--app-log`), log prefixes (`app:`, `traffic-models:`) and docs.

## Alternatives
- **Keep finding one app automatically.** Simplest for that app's users, but ties ClearanceLink to an app whose
  developers don't support it, and implies an endorsement that doesn't exist.
- **A list of supported apps,** each with its own folder lookup and log path. Each entry is a claim of support we
  can't back up, and needs testing per app; picking the .exe covers them all.

## Consequences
- Users pick the app once; nothing is guessed.
- Apps that aren't Unity apps get no pushback from the log (the PUSH page waits for nothing) and no automatic SimBrief
  ID (`settings simbrief-id` still works). Their log isn't in the support zip.
- An app whose connections we haven't recorded is treated as connected on its first connection, so the stand-in may
  close earlier than that app expects. Record it before changing this (AGENTS.md rule 1).
