# XPConnect ↔ X-Plane plugin protocol

The X-Plane plugin (`plugin/`, ADR-0004) draws BATC's AI traffic with XPMP2 and CSL models and reports the terrain
height under each aircraft. XPConnect and the plugin talk over UDP on the local machine:

| Direction | Address | Notes |
|---|---|---|
| XPConnect → plugin | `127.0.0.1:49710` | The plugin binds this port. |
| plugin → XPConnect | `127.0.0.1:49711` | XPConnect binds this port. |

Every datagram holds one or more lines of UTF-8 text separated by `\n`. Fields are separated by single spaces.
Numbers use `.` as the decimal point. Unknown message types are ignored (and logged once), so either side can be
newer than the other.

## XPConnect → plugin

| Line | Meaning |
|---|---|
| `hello <version>` | Sent every second. If the plugin hears nothing for 10 s it removes all its aircraft (XPConnect closed). |
| `add <id> <icaoType> <icaoAirline> <livery> <label...>` | Create aircraft `id` (1..16777215). `icaoAirline` and `livery` are `-` when unknown. `label` (rest of the line) is shown next to the aircraft, for example the registration. Re-sending `add` for an existing id changes its model. |
| `pos <id> <lat> <lon> <altFt> <hdgTrue> <pitch> <bank> <gsKt> <onGround> <gear> <flaps> <spoilers> <thrust> <lights>` | Latest state. `altFt` is MSL; when `onGround` is `1` the plugin puts the wheels on X-Plane's terrain and ignores `altFt`. `gear`, `flaps`, `spoilers`, `thrust` are 0..1. `lights` is a bit mask: 1 nav, 2 beacon, 4 strobe, 8 landing, 16 taxi. |
| `offset <id> <feet> [<pitch>]` | Per-type height (feet, + raises) and parked pitch (degrees, nose up +) for aircraft `id`, from `ground_offset_ft` and `ground_pitch` in `mapping/traffic-models.yaml`, sent after each `add`. Plugin 0.4.0+ uses them only for models it cannot fit to their own wheels, and XPConnect then sends the pitch here rather than in each `pos`; to older plugins XPConnect sends `<feet>` only (0.2.0+) and puts the pitch in `pos`. |
| `del <id>` | Remove aircraft `id`. |
| `clear` | Remove all aircraft. |

On the ground the plugin also pitches each aircraft to the slope of the terrain under it (probed 8 m ahead and
behind, at most 4°), added to the `pitch` XPConnect sends (plugin 0.2.0+).

Plugin 0.4.0+ parks each CSL model on its own wheels. The first time a model is drawn it reads the model's `.obj`
files in the background (found from its `OBJ8` lines in `xsb_aircraft.txt`; a package can span several folders with
the same `EXPORT_NAME`, as Bluebell's `__Bluebell_Airbus` does over `BB_Airbus`, `BB_Airbus1` and `BB_Airbus2`), and
poses it with the gear down (`plugin/src/GroundFit.cpp`, `ReadPosedObj`): every `ANIM_` transform applied in the
OBJ8 order (the last command applies to a point first), `gear_ratio` and gear-door datarefs at 1, the main tyre
deflection at the value that levels a tilting bogie (777, A340, A380; 0 for other models), everything else at 0, and
parts hidden by `ANIM_hide`/`ANIM_show` left out. Bluebell keeps its gear retracted in the file and lowers it by
animation (B738 and E190 read on Ben's PC, 2026-10-03), so the raw `VT` points are not where the wheels are drawn.
The nose wheel is the lowest point the `gear_ratio` animations move in the front 35% of the model, the main wheels
the lowest one behind that. On the ground the plugin adds the pitch that puts both on level ground, sets the height
from the wheels so the lower one touches (this also keeps the main gear down while BATC rotates for take-off), and
draws the model with the tyre deflection it measured. Models it cannot fit (no gear animation, such as fixed-gear
Cessnas and helicopters, or a fit beyond 3°) use XPConnect's per-type pitch and height on XPMP2's vertical offset.

Checked against Ben's Bluebell library (2,027 models): the measured wheels agree with each model's own `VERT_OFFSET`
within 5 cm for 1,247 models, and parked pitches are 0.2° to 2.6° nose down (B738 -1.98°, E190 -2.59°, A320 -1.33°,
DH8D -0.69°). Plugin 0.3.0 and 0.3.1 read the raw `VT` points without posing the gear and got the 737 wrong and most
other models not at all; 0.3.2 and 0.3.3 only logged the fit. `xpconnect-groundfit <CSL folder>...` (in the plugin
build's `tools` folder) prints the fit for every model of a CSL library as CSV. The X-Plane log gets one line per model:
`XPConnect: ground fit <csl id>: nose wheel 0.52 m above mains, pitch -1.98 deg, wheels 3.33 m below the model origin
(CSL offset 3.31 m)` or `XPConnect: ground fit <csl id>: none (<reason>), drawn level`.

BATC writes each moving AI aircraft about 55 times a second (one every 16.7 ms in the 2026-09-30 MSFS recording).
XPConnect sends the latest state 10 times a second, and a parked aircraft whose state has not changed only every 2 s.
Plugin 0.3.1+ draws each aircraft one update interval behind and blends between its last two `pos` lines (position,
height, heading the short way round, pitch, bank, gear, flaps), so turns, climbs and the flare are smooth; a new `pos`
blends on from where the aircraft is drawn, so nothing jumps. If updates stop, it moves on as it moved between the last
two updates, for at most 1 s (0.3.2+; 0.3.1 used `gsKt`, which is BATC's airspeed and not always 0 for a stopped
aircraft, so parked aircraft slid forward and back). Positions more than 1 km apart are drawn at once.

Terrain probes are kept cheap: on the ground an aircraft is probed again only when it has moved 25 cm or turned 1°,
or every 5 s when parked; in the air every 0.25 s (for the `ground` report).

## plugin → XPConnect

| Line | Meaning |
|---|---|
| `status <version> <aircraft> <models> <cslFolders>` | Sent every second. `models` is the number of CSL models loaded (0 means traffic cannot be drawn: no CSL package found). `cslFolders` is a `;`-separated list (rest of the line), `-` if none. |
| `ground <id> <elevFt>` | Terrain elevation (MSL, feet) under aircraft `id`, from X-Plane's terrain probe. Sent about every 0.25 s per aircraft. This is what BATC's `GROUND ALTITUDE` for an AI object should return. |
| `error <id> <message...>` | The aircraft could not be created (for example no CSL model matched). |

## CSL models

The plugin loads every CSL package it finds in these folders (all that exist, in this order):

1. Folders listed one per line in `Resources/plugins/XPConnect/csl_paths.txt` (absolute, or relative to the X-Plane folder).
2. `Resources/plugins/XPConnect/Resources/CSL`
3. `Custom Data/CSL`
4. The CSL folders of LiveTraffic, xPilot and X-IvAp (`Resources/plugins/<plugin>/Resources/CSL`).

No CSL models are shipped with XPConnect; their licences allow free download but not redistribution.

## Settings in X-Plane

Plugins > XPConnect > Show tail labels turns the labels (BATC's registrations) on or off. They are off by default;
the choice is kept in `Resources/plugins/XPConnect/settings.txt` (`labels=1`), which `xpconnect plugin install` keeps.
