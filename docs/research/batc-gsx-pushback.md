# BeyondATC's GSX pushback and Better Pushback

Research for ADR-0013, 2026-10-05.

## What BeyondATC does with GSX (MSFS)
Source: pass-through recording of BeyondATC 1.8.21 against MSFS 2024 with GSX, Ben's PC
(`recordings/local/20260930-174605`, not committed), and BeyondATC's `Player.log`.
- Subscribes to `L:FSDT_GSX_PUSHBACK_STATUS` once a second.
- On pushback approval writes `L:FSDT_GSX_MENU_OPEN` = 1, then `L:FSDT_GSX_MENU_CHOICE` = 4 ("Request pushback").
- Watches the status go 0, 1, 2, 3 and back to 0.
- Finds GSX through `HKCU\Software\Fsdreamteam` value `root` and reads the menu text from
  `<root>\MSFS\fsdreamteam-gsx-pro\html_ui\InGamePanels\FSDT_GSX_Panel\menu`.
- Against ClearanceLink (no GSX) its log says `Didn't run Push Seq Task as menu state incorrect! (Current state: 0)`
  then `Pushback selection timed out`. Harmless.
- The direction is not in any GSX call. It is in the log as plain text:
  `[Instruction] <callsign>, Brisbane Ground, pushback and engine start approved. Face east.` and the readback
  `[Instruction] Pushback approved, Face east, <callsign>.` Other aircraft's calls are `[LocalVoiceInput]` lines.

## Better Pushback 1.14 (olivierbutler fork, CDDL)
Source: the fork's source code and a test over the Web API on Ben's PC.
- Commands: `BetterPushback/start`, `stop`, `connect_first`, `disconnect`, `acknowledge_clear`, `start_planner`,
  `manual_push_start` / `manual_push_start_no_yoke` (start, and pause while pushing), `manual_push_left` /
  `manual_push_right` (steering in steps of 10% of the nose wheel's limit).
- Datarefs: `bp/started`, `bp/connected`, `bp/op_complete`, `bp/planner_open`.
- No command or dataref sets a planned route or final heading; saved routes are picked in the planner by key or click.
  The old `BetterPushback_routes.dat` isn't read any more.
- The manual push stops if the toe brakes go over 0.03 or the parking brake is set.
- After `stop` it waits for the parking brake, then for `disconnect`, then for the wing walker's clear signal to be
  acknowledged (`acknowledge_clear`), unless the aircraft's own settings disconnect when done.

## Test on Ben's PC (2026-10-05, X-Plane 12.4.3, YBBN bay 28)
- Citation X: the tug connected and the push began about 7 s after the parking brake was released. About 1.1 m/s
  straight, 0.6 m/s at 50% steering. `manual_push_right` swings the tail right (heading falls), about 4 deg/s at 50%.
  After centring it kept turning about 6 degrees. Pausing slows to a stop in about 5 s.
- Aerobask Phenom 300: connected but never moved, also with Better Pushback's own planned push. Not ClearanceLink.

## Phenom 300 vs Citation X (2026-10-06, logged twice a second)
- The Web API read `faxil_plug_acf`, `fside_plug_acf` and `M_plug_acf` as 0 in both aircraft, and only the steering
  override was on. Better Pushback's source (BetterPusbackMod, `src/bp.c`, `push_at_speed`) does push through
  `faxil_plug_acf` and `N_plug_acf`: each frame, before the flight model, it adds its force to the value already
  there, up to 5 kN per tonne of aircraft mass (about 35 kN for the Phenom). The 0 readings are probably a sampling
  effect between frames, since the Citation moved with the same readings.
- Citation X: moving within 1 s of Better Pushback's "pushing" stage, 23 m at 1.1 m/s.
- Phenom 300: Better Pushback reached "pushing" but its tug's drive never turned and nothing moved. No brake, chock,
  skid or override dataref was set.
- The Phenom's wheels aren't held: with the brake off it rolled forward on its own engines at about 18% throttle
  (about 0.7 kN over idle). Its gear force reads about -2.5 kN in every state, an offset in its gear model.
- X-Plane's own pushback only serves airliners and refused the Phenom.
- So the stall is between Better Pushback and this aircraft. Likely cause (inferred, not proven): the Phenom's own
  plugin writes `faxil_plug_acf` (sets it, not adds to it) later in the same frame, so Better Pushback's force is lost.
  Better Pushback's "Acf plugin to exclude" preference disables the chosen plugin from push start to push stop, which
  would test this. Better Pushback 1.15 behaves the same (Ben, 2026-10-06). Not ClearanceLink; the PUSH page shows NOT MOVING: PRESS STOP after 40 s with the brake off.
