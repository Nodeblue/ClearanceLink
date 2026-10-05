# ADR-0013: Pushback goes to Better Pushback, only when X-Plane has it

Date: 2026-10-06. Status: accepted (Ben, 2026-10-06: "build and i will test").

## Context
In MSFS, BeyondATC asks GSX for pushback: it writes `L:FSDT_GSX_*` L-vars and reads `PUSHBACK_STATUS`
(`docs/research/batc-call-inventory.md`). ClearanceLink accepts those writes and does nothing with them. X-Plane has
no GSX; the free Better Pushback plugin is the common pushback add-on.

Tests on Ben's PC on 2026-10-06 (X-Plane 12, Better Pushback 1.14, YBBN), driven only by Web API commands
(`recordings/local/bp-manual-test-cx.*`, not committed):
- Citation X: `BetterPushback/manual_push_start_no_yoke` once calls the tug; after the pilot releases the parking
  brake it connects and pushes straight back at about 1.1 m/s. Each `manual_push_right`/`manual_push_left` moves the
  steer angle 10°; at 50° the aircraft turns about 4°/s at about 0.6 m/s, tail right for `manual_push_right`. The same
  command pauses (the aircraft stops in about 5 s), and `BetterPushback/stop` ends the push. The tug then waits at
  "waiting_to_disconnect" for one more pilot action.
- Phenom 300 (Aerobask): every command is accepted and the tug connects, but the aircraft never moves, in Better
  Pushback's own automatic mode too. Being installed does not mean it works with every aircraft.

Ben asked (2026-10-06) that ClearanceLink find out whether Better Pushback is installed, say so in the window, and send
no pushback commands without it.

## Decision
- ClearanceLink asks X-Plane's Web API whether the command `BetterPushback/start` exists: when X-Plane connects and
  every 30 s after (the plugin can be enabled, disabled or reloaded while X-Plane runs). The answer is not cached with
  the other command ids and is forgotten when X-Plane disconnects (ids change with every X-Plane run).
- STATUS shows it on the left of its fifth row, PUSHBACK: BETTER PB (green), NOT FOUND (white: optional, not a fault),
  or `-` while X-Plane is not connected. RUNNING FOR, which was there, moves to ABOUT. The web status page's JSON has
  `pushback`.
- `BetterPushback.Available` is the gate the GSX translation will use: without Better Pushback, BeyondATC's GSX
  requests stay ignored as today and nothing is sent to X-Plane.

Alternatives considered:
- Looking for the plugin's folder in `Resources/plugins`: says nothing about whether X-Plane loaded it (disabled in
  Plugin Admin, wrong version) and needs the X-Plane folder; the Web API answers what is loaded.
- Reading the version from X-Plane's Log.txt: possible later, but the log is rewritten on every start and the line's
  wording belongs to the plugin.
- Checking only once per connection: misses enabling or reloading the plugin while X-Plane runs, which Ben did today.

## Consequences
- One extra REST request every 30 s while X-Plane is connected.
- Not yet: the GSX translation itself, and noticing a push that does not move (the Phenom case): the plan is to stop
  sending steer commands and say so when the aircraft has not moved about 30 s after `bp/connected` is 1.
