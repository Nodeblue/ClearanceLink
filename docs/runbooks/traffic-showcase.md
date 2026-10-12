# Traffic showcase: check CSL models without the ATC app

`clearancelink showcase` draws a fixed scene through the X-Plane plugin so models, liveries, wheels, propellers and
steering can be checked in one place, the same way every time.

## Run it
1. Close ClearanceLink (the showcase uses the plugin link instead of it; it clears the plugin's aircraft when it starts).
2. Start X-Plane with the ClearanceLink plugin and park somewhere you can see the runway, e.g. at YBBN.
3. `clearancelink showcase` (defaults: YBBN, its longest runway from the first end, the longest named taxiway).
4. Ctrl+C removes the aircraft.

What you get:
- **Line-up:** one aircraft of each type, nose to tail on the runway centre line from the threshold, grouped Light,
  Regional jet, Narrow body, Wide body, Heavy, with a wider gap between groups. Gear down, engines stopped, nav lights.
  With imported FSLTL liveries (`clearancelink fsltl import`) each type gets a real airline livery, Australian airlines
  first; without, the plugin picks a CSL model by type.
- **Taxiing:** 8 of them (one per group in turn) going up and down a taxiway at 15 kt with engines running, turning
  round at each end: wheels roll, propellers turn, nose wheels steer.

- **Model variants (parallel runway):** with imported FSLTL liveries, one aircraft per converted model the main line-up does not show (engine and winglet variants, freighters, other model makers; at most 2 per type, per-livery model copies counted once), on the other runway within 10 degrees, e.g. 01L at YBBN. The console lists each with its model name.

## Options
| Option | Default | |
|---|---|---|
| `--airport` | YBBN | any airport in X-Plane's scenery |
| `--runway` | longest runway, first end | runway end to line up from, e.g. `19L` |
| `--taxiway` | longest named taxiway | taxiway name from apt.dat, e.g. `A` |
| `--per-group` | 9 | types per size group |
| `--taxiers` | 8 | aircraft taxiing |
| `--gap` | 25 | metres between aircraft (3x between groups) |
| `--speed` | 15 | taxi speed in knots |
| `--variants-runway` | the parallel runway | runway end for the model variants, or `none` |

The console lists every aircraft by group, type and airline, so what you see can be matched to a model.
