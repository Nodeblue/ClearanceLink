# XPConnect to X-Plane plugin: traffic feed (draft)

Status: draft for Phase 3 (drawing BATC's AI traffic in X-Plane). Nothing implements it yet.

## Why
BATC creates and flies its own traffic through SimConnect (`docs/protocol/simconnect.md`, "AI traffic"). XPConnect
keeps that state (`TrafficRegistry`), but X-Plane's Web API cannot create aircraft. A small X-Plane plugin built on
XPMP2 draws them with CSL models and gives XPConnect real terrain heights, which BATC asks for to keep its traffic on
the ground.

## Transport
- TCP on `127.0.0.1:5578`. XPConnect listens; the plugin connects and reconnects every 2 s when XPConnect is not
  running (XPConnect may start before or after X-Plane).
- Newline-delimited JSON, UTF-8, one message per line. At 20 updates/s for ~60 aircraft this is about 1 MB/s locally,
  which is fine; switch to a binary frame only if profiling says so.
- Either side ignores message types it does not know (versioning by addition).

## XPConnect to plugin
`hello`, once on connect:
```json
{"type":"hello","version":1,"xpconnect":"0.4.0"}
```

`traffic`, about 20 times a second, the full current list (removal = absence; no separate remove message):
```json
{"type":"traffic","time":1727765520.125,"aircraft":[
  {"id":201326616,"icao":"B738","airline":"QFA","livery":"Qantas","tail":"VHXZE","model":"FSLTL_B738_Qantas",
   "lat":-27.38123,"lon":153.12143,"altFt":20.5,"onGround":true,"heading":263.1,"pitch":0.0,"bank":0.0,"ias":0,
   "gear":1.0,"flaps":0.0,"spoilers":0.0,"thrust":0.0,
   "lights":{"nav":true,"beacon":true,"strobe":false,"landing":false,"taxi":true},"engines":true}]}
```
- `pitch` and `bank` in X-Plane's sign convention (positive nose up, positive right wing down); XPConnect converts
  from MSFS's.
- `altFt` is MSL. When `onGround` is true the plugin should clamp to terrain (XPMP2 does this) instead of trusting it.
- `icao`/`airline` are XPConnect's best guess from the MSFS title (`FSLTL_B738_Qantas` -> B738, QFA); `model` is passed
  through for logging. Ground vehicles (pushback tugs) are not sent in version 1.

`probe`, when BATC asks for ground altitude under an AI aircraft:
```json
{"type":"probe","id":7,"points":[{"lat":-27.38123,"lon":153.12143}]}
```

## Plugin to XPConnect
`hello`:
```json
{"type":"hello","version":1,"plugin":"0.1.0","xplane":"12.4.3","csl":{"folders":["..."],"models":2381}}
```

`probeResult` (`elevM` metres MSL from `XPLMProbeTerrainXYZ`, null where the probe missed):
```json
{"type":"probeResult","id":7,"elevM":[3.9]}
```

`status`, every few seconds: aircraft drawn and models missing, for the status page.
```json
{"type":"status","drawn":45,"noModel":["A339","AT73"]}
```

## Install
`xpconnect plugin on|off|status` copies the plugin folder (`XPConnect/win_x64/XPConnect.xpl` plus `Resources/`) to
`<X-Plane>/Resources/plugins/XPConnect`, or removes it. CSL packages are not shipped; the plugin looks in its own
`Resources/CSL` and in folders other traffic plugins use, and the status page says when none is found.
