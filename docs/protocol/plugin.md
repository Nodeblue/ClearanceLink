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
| `del <id>` | Remove aircraft `id`. |
| `clear` | Remove all aircraft. |

Between `pos` lines the plugin moves each aircraft on along its heading at `gsKt` (for at most 1 s), so a
few updates a second are enough for smooth movement. BATC itself sends about 20 a second.

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
