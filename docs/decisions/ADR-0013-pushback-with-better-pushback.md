# ADR-0013: Pushback with Better Pushback, from the ATC app's clearance

Date: 2026-10-05. Status: accepted.

## Context
In MSFS, the ATC app (1.8.21) drives GSX for pushback: it subscribes to `L:FSDT_GSX_PUSHBACK_STATUS`, opens GSX's menu
and picks item 4 ("Request pushback") through `L:FSDT_GSX_MENU_OPEN` / `L:FSDT_GSX_MENU_CHOICE`, and reads GSX's menu
text from a file in GSX's own folder, found through the registry (`HKCU\Software\Fsdreamteam`, `root`). X-Plane has no
GSX. Ben uses Better Pushback 1.14 (the maintained olivierbutler fork) and asked whether the ATC app's pushback, including
its "face east" instruction, could drive it. Research and test results: `docs/research/atc-app-pushback.md`.

## Decision
- **The clearance comes from the ATC app's log.** The ATC app writes each instruction to its `Player.log` as plain text
  (`[Instruction] ..., pushback and engine start approved. Face east.`). ClearanceLink follows the log (new lines only)
  and shows the approval and direction on a new PUSH page. The ATC app's GSX L:var writes stay ignored.
- **The approval calls the tug, as with GSX, and the pilot can switch that off.** In MSFS the ATC app requests GSX's
  pushback itself at the approval; the pilot only releases the parking brake. TUG on the PUSH page is AUTO by default (Ben
  chose this on 2026-10-05 after first trying a key-only version) or MANUAL, saved as `pushAutomatic` in
  settings.json. MANUAL waits for CALL TUG; STATUS says PUSHBACK APPROVED: PUSH meanwhile. TAIL LEFT, TAIL RIGHT and
  NO TURN override the clearance until the tug starts moving (a called push is planned again). Either way the tug only pushes once the brake is off.
- **Better Pushback's manual push, closed loop, over the Web API.** `BetterPushback/manual_push_start_no_yoke` starts
  (and pauses) the push, `manual_push_left/right` steer in 10% steps, `stop` ends it, and once the pilot sets the
  parking brake `disconnect` and `acknowledge_clear` send the tug away. ClearanceLink watches position, heading and
  ground speed and decides when to turn, centre and stop. Better Pushback is found by its `bp/started` dataref; without
  it the PUSH page says BETTER PUSHBACK MISSING and nothing is sent.
- **The push ends on a taxiway.** The taxiway behind the aircraft (apt.dat 1202 edges, the stand's own lead-in left
  out) sets how far to push before turning and the final heading, the way along it closest to the cleared direction.
  Without one: 15 m straight, then a turn toward the direction (at most 135 degrees). A push with no turn goes about
  one aircraft length, taken as 2.5 wheelbases from `tire_z_no_deflection` (15 to 60 m), as X-Plane has no reliable
  length dataref.

## Alternatives
- **Pretend to be GSX** (registry key, menu file, L:var status 1-2-3-0 back to the ATC app). Writes into the registry and
  a fake GSX folder, and GSX's menu protocol isn't documented; the direction still isn't in it.
- **Better Pushback's planned push.** Its routes are saved per stand and chosen with a key or a click in its planner;
  there is no command or dataref to set a route or heading.
- **A change to Better Pushback** adding a route command. Possible later; the manual push works with 1.14 today.

## Consequences
- Tested by hand on Ben's PC (Citation X at YBBN, commands sent over the Web API): straight push, a 90 degree turn
  with the tail right, pause and stop. The PUSH page with its key pushed the Citation X back 15 m to face
  southwest and disconnected the tug (2026-10-05); the automatic call worked there the same day.
- Better Pushback can't move some aircraft at all (the Aerobask Phenom 300, even with its own planned push); the page
  says NOT MOVING: PRESS STOP after 40 s with the brake off.
- Depends on the wording of the ATC app's log lines; a change there stops the clearance showing, and the TAIL keys
  still work.
