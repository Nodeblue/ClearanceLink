# ClearanceLink ↔ X-Plane plugin protocol

The X-Plane plugin (`plugin/`, ADR-0004) draws BATC's AI traffic with XPMP2 and CSL models and reports the terrain
height under each aircraft. ClearanceLink and the plugin talk over UDP on the local machine:

| Direction | Address | Notes |
|---|---|---|
| ClearanceLink → plugin | `127.0.0.1:49710` | The plugin binds this port. |
| plugin → ClearanceLink | `127.0.0.1:49711` | ClearanceLink binds this port. |

Every datagram holds one or more lines of UTF-8 text separated by `\n`. Fields are separated by single spaces.
Numbers use `.` as the decimal point. Unknown message types are ignored (and logged once), so either side can be
newer than the other.

## ClearanceLink → plugin

| Line | Meaning |
|---|---|
| `hello <version>` | Sent every second. If the plugin hears nothing for 10 s it removes all its aircraft (ClearanceLink closed). |
| `add <id> <icaoType> <icaoAirline> <livery> <label...>` | Create aircraft `id` (1..16777215). `icaoAirline` and `livery` are `-` when unknown. `label` (rest of the line) is shown next to the aircraft, for example the registration. Re-sending `add` for an existing id changes its model. |
| `pos <id> <lat> <lon> <altFt> <hdgTrue> <pitch> <bank> <gsKt> <onGround> <gear> <flaps> <spoilers> <thrust> <lights>` | Latest state. `altFt` is MSL; when `onGround` is `1` the plugin puts the wheels on X-Plane's terrain and ignores `altFt`. `gear`, `flaps`, `spoilers`, `thrust` are 0..1 (plugin 0.5.2+ clamps them). `lights` is a bit mask: 1 nav, 2 beacon, 4 strobe, 8 landing, 16 taxi; plugin 0.5.2+ ignores a `pos` whose `lights` is not a whole number 0..31. |
| `offset <id> <feet> [<pitch>]` | Per-type height (feet, + raises) and parked pitch (degrees, nose up +) for aircraft `id`, from `ground_offset_ft` and `ground_pitch` in `mapping/traffic-models.yaml`, sent after each `add`. Plugin 0.4.0+ uses them only for models it cannot fit to their own wheels, and ClearanceLink then sends the pitch here rather than in each `pos`; to older plugins ClearanceLink sends `<feet>` only (0.2.0+) and puts the pitch in `pos`. |
| `del <id>` | Remove aircraft `id`. ClearanceLink sends it on 3 syncs in a row, as UDP can drop it; plugin 0.4.1+ also removes an aircraft that has had no `add` or `pos` for 15 s. |
| `clear` | Remove all aircraft. ClearanceLink sends it when it starts, so a plugin still drawing a previous run's aircraft drops them. |

On the ground the plugin also pitches each aircraft to the slope of the terrain under it (probed 8 m ahead and
behind, at most 4°), added to the `pitch` ClearanceLink sends (plugin 0.2.0+).

Plugin 0.4.0+ parks each CSL model on its own wheels. The first time a model is drawn it reads the model's `.obj`
files in the background (found from its `OBJ8` lines in `xsb_aircraft.txt`; a package can span several folders with
the same `EXPORT_NAME`, as Bluebell's `__Bluebell_Airbus` does over `BB_Airbus`, `BB_Airbus1` and `BB_Airbus2`; plugin 0.4.2
patches XPMP2 so it loads those models too, see `plugin/cmake/xpmp2-split-packages.patch`), and
poses it with the gear down (`plugin/src/GroundFit.cpp`, `ReadPosedObj`): every `ANIM_` transform applied in the
OBJ8 order (the last command applies to a point first), `gear_ratio` and gear-door datarefs at 1, the bogie dataref
at the value that levels a tilting bogie (777, A340, A380; 0 for other models), everything else at 0, and
parts hidden by `ANIM_hide`/`ANIM_show` left out. Bluebell keeps its gear retracted in the file and lowers it by
animation (B738 and E190 read on Ben's PC, 2026-10-03), so the raw `VT` points are not where the wheels are drawn.
The nose wheel is the lowest point the `gear_ratio` animations move in the front 35% of the model, the main wheels
the lowest one behind that. On the ground the plugin adds the pitch that puts both on level ground, sets the height
from the wheels so the lower one touches (this also keeps the main gear down while BATC rotates for take-off), and
draws the model with the bogie value it measured. The bogie dataref is XPMP2's
`libxplanemp/gear/tire_vertical_deflection_mtr`, or World Traffic's `cjs/world_traffic/main_gear_deflection`, which
Bluebell's virtual-airline repaints use (95 models; B772_PAY drawn at touchdown tilt at YBBN, 2026-10-03). The plugin
adds that one to XPMP2 with `XPMPAddModelDataRef`; the other World Traffic gear datarefs stay at 0. The bogie is the
rotation keyed to either dataref that turns furthest (smaller ones, under 5°, are links and struts), searched over all
of a model's files before any is posed. Its level value is found from the posed wheels, not from the angle: the value
at which the lowest points of the bogie's front and rear halves are at the same height. Bluebell's 747 models its
wing-gear bogies already tilted and turns them 30° to 73°, so the angle never reads 0 when they are level (YBBN,
2026-10-03). Models it cannot fit (no gear animation, such as fixed-gear
Cessnas and helicopters, or a fit beyond 3°) use ClearanceLink's per-type pitch and height on XPMP2's vertical offset.

Checked against Ben's Bluebell library (2,027 models): the measured wheels agree with each model's own `VERT_OFFSET`
within 5 cm for 1,247 models, and parked pitches are 0.2° to 2.6° nose down (B738 -1.98°, E190 -2.59°, A320 -1.33°,
DH8D -0.69°). Plugin 0.3.0 and 0.3.1 read the raw `VT` points without posing the gear and got the 737 wrong and most
other models not at all; 0.3.2 and 0.3.3 only logged the fit. `clearancelink-groundfit <CSL folder>...` (in the plugin
build's `tools` folder) prints the fit for every model of a CSL library as CSV. The X-Plane log gets one line per model:
`ClearanceLink: ground fit <csl id>: nose wheel 0.52 m above mains, pitch -1.98 deg, wheels 3.33 m below the model origin
(CSL offset 3.31 m)` or `ClearanceLink: ground fit <csl id>: none (<reason>), drawn level`.

BATC writes each moving AI aircraft about 55 times a second (one every 16.7 ms in the 2026-09-30 MSFS recording).
ClearanceLink sends the latest state 10 times a second, and a parked aircraft whose state has not changed only every 2 s.
Plugin 0.3.1+ draws each aircraft one update interval behind and blends between its last two `pos` lines (position,
height, heading the short way round, pitch, bank, gear, flaps), so turns, climbs and the flare are smooth; a new `pos`
blends on from where the aircraft is drawn, so nothing jumps. If updates stop, it moves on as it moved between the last
two updates, for at most 1 s (0.3.2+; 0.3.1 used `gsKt`, which is BATC's airspeed and not always 0 for a stopped
aircraft, so parked aircraft slid forward and back). Positions more than 1 km apart, or a move faster than twice
`gsKt` plus 15 m/s, are drawn at once: BATC placing a parked aircraft 100 m+ away was slid across and the move-on then
overshot, back and forth, until 0.4.3 (YBBN, 2026-10-03). Since 0.4.3 the move-on speed comes from the last two `pos`
lines received (not the blended point), and is 0 when they are more than 1 s apart (ClearanceLink's keep-alives).

Terrain probes are kept cheap: on the ground an aircraft is probed again only when it has moved 25 cm or turned 1°,
or every 5 s when parked; in the air every 0.25 s (for the `ground` report). Since 0.5.2 a moving aircraft is probed
at most every 0.1 s (sooner only once it has moved 8 m, beyond the measured slope), and between probes its height
follows the slope measured at the last one, so it does not step on taxiway slopes; before, anything faster than
about 15 m/s was probed (three probes) every frame.

## plugin → ClearanceLink

| Line | Meaning |
|---|---|
| `status <version> <aircraft> <models> <cslFolders>` | Sent every second. `models` is the number of CSL models loaded (0 means traffic cannot be drawn: no CSL package found). `cslFolders` is a `;`-separated list (rest of the line), `-` if none. |
| `ground <id> <elevFt>` | Terrain elevation (MSL, feet) under aircraft `id`, from X-Plane's terrain probe. Sent about every 0.25 s per aircraft. This is what BATC's `GROUND ALTITUDE` for an AI object should return. |
| `error <id> <message...>` | The aircraft could not be created (for example no CSL model matched). |

## CSL models

The plugin loads every CSL package it finds in these folders (all that exist, in this order):

1. Folders listed one per line in `Resources/plugins/ClearanceLink/csl_paths.txt` (absolute, or relative to the X-Plane folder).
2. `Resources/plugins/ClearanceLink/Resources/CSL`
3. `Custom Data/CSL`
4. The CSL folders of LiveTraffic, xPilot and X-IvAp (`Resources/plugins/<plugin>/Resources/CSL`).

No CSL models are shipped with ClearanceLink; their licences allow free download but not redistribution.

## Settings in X-Plane

Plugins > ClearanceLink > Show tail labels turns the labels (BATC's registrations) on or off. They are off by default;
the choice is kept in `Resources/plugins/ClearanceLink/settings.txt` (`labels=1`), which `clearancelink plugin install` keeps.
