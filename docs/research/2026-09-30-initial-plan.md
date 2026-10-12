# ClearanceLink: the ATC app on X-Plane 12, research and implementation plan

_Written 2026-09-30. Facts marked **(verified)** come from a source linked at the bottom; facts marked **(inferred)** are my reading of the evidence and are the first things the Phase 0 spike checks._

## 1. Verdict first

**It looks feasible for ATC use, with one real risk to prove in the first session.** No show-stopper found on paper.

| # | Question | Verdict | Why |
|---|----------|---------|-----|
| 1 | Can the ATC app talk to something that is not MSFS? | **Likely yes, prove first** | The ATC app talks to the sim through SimConnect, a documented client/server protocol over a Windows named pipe or TCP. Open-source client implementations exist (node-simconnect, jSimConnect), so the wire format is known. We become the SimConnect *server*. Unknown: whether the ATC app also checks for the MSFS process or MSFS files on disk. |
| 2 | Airport, runway, taxiway, parking and procedure data | **Doable, biggest chunk of work** | The ATC app's FAQ says airport data, approaches and waypoints come "directly from your simulator". That is almost certainly the SimConnect Facilities API **(inferred)**. We must answer those requests from X-Plane's own data (`apt.dat` taxi networks and gates, CIFP procedures, `earth_fix`/`earth_nav`). Everything needed exists in X-Plane; the work is translation. |
| 3 | SimBrief | **Not a problem** | The ATC app pulls the SimBrief OFP itself using your Pilot ID **(verified)**. It never gets the plan from the sim. You load the same OFP into the X-Plane FMS as usual. Nothing to build. |
| 4 | Position, altitude, heading, speed, on-ground, radios, transponder, pause | **Easy** | All exist as X-Plane datarefs/commands, readable and writable through X-Plane's built-in Web API (REST + WebSocket, 12.1.1+). No plugin needed for this. |
| 5 | Voice, speech recognition, push-to-talk | **Probably nothing to do** | The ATC app does speech in the cloud and in its own `SpeechEngineBasic.exe` **(verified)**; PTT is bound in the ATC app's own window **(inferred)**. Check in Phase 0 that PTT does not route through sim input events. |
| 6 | The ATC app in-sim toolbar | **Skip** | It is an MSFS Community-folder panel. Use the ATC app desktop window (or a second monitor/VR window). |
| 7 | AI traffic (stretch) | **Stub now, real later via plugin** | The ATC app injects its own traffic through SimConnect AI calls using MSFS model packages (FSLTL/AIG) **(verified)**. X-Plane has no external API for this, so it needs an X-Plane plugin using the proven XPMP2 library (what LiveTraffic/xPilot use) plus CSL models. Until then we accept the calls, track the objects, and draw them on our dashboard only. Simplest for ATC-only use: turn traffic off in the ATC app. |
| 8 | Do we need X-Plane 12.4.4? | **No** | 12.4.4's SDK additions are UI/graphics (Panel Graphics, ImGui, CEF, XLua 2.0, object attachment) plus a `sim/atc/transponder_assigned` dataref **(verified)**. Nothing for ATC or traffic. Target **12.4.0+** (Web API v3). CEF could one day host a ATC-app-like in-sim panel, but that is optional polish. |
| 9 | Legal/EULA | **Low risk, your call** | We do not modify the ATC app; we implement the other end of a documented protocol. Do not redistribute Microsoft's `SimConnect.dll`. Worth a glance at the ATC app's terms. |

### How the ATC app is built (what the evidence shows)
- The app's .exe is a **Unity** app (UnityCrashHandler64.exe, logs in `%USERPROFILE%\AppData\LocalLow\<company>\<product>\Player.log`) **(verified)**.
- A separate **the app's helper program** ships next to it **(verified)**. The name strongly suggests a SimConnect-to-WebSocket bridge: the Unity app talks WebSocket to it, and it talks SimConnect to MSFS **(inferred)**.
- Other helpers: `generateATIS.exe`, `SpeechEngineBasic.exe`, `sqlite3.exe` (probably its local nav cache) **(verified file list)**.
- Needs: SimBrief Pilot ID, internet, MSFS ATC/traffic turned off **(verified)**.

That gives us two places to plug in:

```
app .exe (Unity) --WebSocket--> helper program --SimConnect pipe/TCP--> [MSFS]
                                                                           ^
                                   we replace MSFS here:  ClearanceLink ---+--> X-Plane 12 Web API (+ later plugin)
```

**Chosen: emulate the SimConnect server** (right-hand side). It needs no changes to the ATC app's files, the protocol is public and stable, and every request is self-describing (clients send SimVar names like `"PLANE ALTITUDE", "feet"` as strings), which makes the "record every call and map it" idea almost free. Fallback if that fails: speak the helper program's WebSocket protocol instead (undocumented, would need capturing, more fragile across ATC app updates).

## 2. Architecture

```
            +-------------------------------- ClearanceLink (one .NET process) -------------------------------+
App   <---->| Transport      Protocol codec     Session/dispatcher      Handlers            Sources          |<----> X-Plane 12
 (helper)   | named pipe  -> decode packets  -> per-client state:    -> SimVar mapping   -> XP Web API      |       Web API :8086
            | + TCP          encode replies     data defs, requests,     Facilities         client (REST+WS)|
            |                                   events, periods          System events      NavData (files) |<----> X-Plane files
            |                                                            AI objects (stub)  (apt.dat, CIFP) |       (read-only)
            |   Recorder (every packet, decoded, JSONL)    Status bus  ---> Dashboard (web UI, localhost)     |
            +-----------------------------------------------------------------------------------------------+
                                                                  later: XP plugin (C++/XPMP2) for AI traffic
```

Key design rules (these are what make it AI-friendly):
1. **Pure core, thin edges.** The protocol codec and the mapping engine are pure functions with no I/O; they get byte-level golden tests.
2. **Everything behind interfaces with fakes.** `IXPlane` has a real Web API implementation and a `FakeXPlane` that plays scripted flights. The whole stack runs on Linux with TCP transport and no simulator, so cloud Claude/Codex sessions can run end-to-end tests.
3. **Mapping as data, not code.** `mapping/simvars.yaml` lists each MSFS SimVar/event, its X-Plane dataref/command, unit conversion, and status (`implemented`, `emulated`, `stubbed`, `unsupported`). Adding a mapping is a one-line YAML change plus a test.
4. **Unknown calls never crash.** Every request type has an explicit policy: implement, stub with success, or reply with a SimConnect exception. Anything unhandled is logged as `UNMAPPED` and counted; `clearancelink report unmapped` prints the to-do list for the next AI session.
5. **Record and replay.** Every the ATC app session is recorded as decoded JSONL. Recordings become regression tests: replay the client side against the server and snapshot the replies. When the ATC app updates and starts asking for something new, the replay diff shows it.
6. **One log stream.** Structured JSON logs (Serilog) with a session id, plus the ATC app's own `Player.log` tailed into the same stream, so an agent can see "the ATC app asked for X, we answered Y, the ATC app logged error Z" in one file.

## 3. Language and tools

**C# on .NET 10 (LTS)** for the middleware, dashboard served by the same process.

Why:
- First-class Windows named pipes and TCP, and binary struct marshaling that maps directly onto the SimConnect SDK header (`SimConnect.h`).
- Runs on Linux too, so cloud agents can build and test everything except the real-pipe/real-app step.
- `dotnet watch` rebuilds and restarts in a few seconds; `dotnet test` for the whole suite should stay well under a minute.
- Codex and Claude are both strong in C#; strong typing catches their mistakes at compile time.
- Single-file `dotnet publish` gives you one `ClearanceLink.exe` to run.

Considered: TypeScript (node-simconnect is a good protocol reference but weaker for a long-running binary server) and Rust (great, but slower iteration for AI agents). The later AI-traffic plugin must be **C++** (X-Plane SDK + XPMP2); it stays small and separate.

Test client: we use **node-simconnect** (an independent client implementation) and, on your PC, a tiny probe built against the real `SimConnect.dll` from the MSFS SDK. Two independent clients catch our own protocol misreadings.

## 4. Fast dev loop on your PC

- X-Plane and the ATC app stay running. ClearanceLink talks to X-Plane only through the Web API in phases 0 to 3, so **rebuilding never restarts X-Plane**.
- `scripts/dev.ps1` runs `dotnet watch` on the host: save a file, it rebuilds and restarts in seconds, and the app's helper program reconnects (to confirm in Phase 0; if it does not, the script restarts the ATC app's bridge for you).
- Dashboard at `http://localhost:5577` shows reconnection instantly.
- `scripts/test.ps1` = unit + replay tests; `scripts/publish.ps1` = single exe.
- Later plugin work: X-Plane's Developer > Reload Plugins, no sim restart.

## 5. Connectivity dashboard (web UI, localhost)

A small web page served by ClearanceLink (browser tab, later a tray icon that opens it). Chosen over a desktop UI because agents can test it with Playwright and screenshots.

- **Tiles:** X-Plane (Web API reachable, version, sim paused/running, aircraft), the ATC app client (connected, app name, protocol version, requests/sec), NavData (index built, AIRAC, airports loaded), Plugin (later).
- **Live SimVar table:** what the ATC app subscribed to, where it maps in X-Plane, current value, status colour.
- **Unmapped / stubbed counters** with the exact calls.
- **Event log** (filterable) and **Record** button to save a session for replay.

## 6. Phases

**Phase 0: Go/no-go spike (first session on your PC).**
1. Minimal SimConnect server on the MSFS named pipe (and TCP): answer the Open handshake, log every packet.
2. Close MSFS, start X-Plane-less ClearanceLink, start the ATC app. Does it connect? Does it refuse without MSFS running or installed? What does it ask for?
3. Try announcing as MSFS 2020 and as MSFS 2024; keep whichever the ATC app is happier with.
4. If you have MSFS installed: a pass-through recorder between the ATC app and real MSFS captures a complete real flight, the best possible spec.
5. Output: `docs/research/atc-app-call-inventory.md` and a go/no-go.

**Phase 1: Core ATC data.** Aircraft state, radios (COM1/2 active/standby, 8.33), transponder code and mode, on-ground, pause/sim-rate, system events (SimStart, Pause, FlightLoaded, 1sec/frame), user callsign/type. X-Plane Web API adapter, dashboard v1, recorder, replay tests.

**Phase 2: Facilities from X-Plane data.** Parse `apt.dat` (custom scenery first, then global), CIFP and fix/nav files into a SQLite index; answer Facilities requests: airports, runways, frequencies, parking/gates, taxi points and paths, SIDs/STARs/approaches. Test by comparing against the recording from Phase 0 where airports match.

**Phase 3: Gate-to-gate hardening.** Scripted test flights (KSEA to KPDX style, one with an ILS, one with a STAR), reconnects, X-Plane restarts, the ATC app updates. Packaged exe, first "use it for real" release.

**Phase 4 (stretch): AI traffic.** 4a: accept AI create/remove/position calls, return object ids, show objects on the dashboard map and feed them into X-Plane's TCAS via the plugin. 4b: render them as 3D models with XPMP2 + CSL packages, map MSFS model titles to CSL ICAO/livery codes. 4c: ground traffic at gates.

## 7. Repository and memory layout

```
clearancelink/
  AGENTS.md              # single source of truth for agents: build, test, rules, where things live
  CLAUDE.md              # one line: @AGENTS.md (so Claude and Codex read the same file)
  README.md
  docs/
    STATUS.md            # what works today, what is stubbed, known issues (updated every PR)
    architecture.md
    decisions/           # ADR-0001-simconnect-server-emulation.md, ADR-0002-dotnet.md, ...
    research/            # this plan, atc-app-call-inventory.md, protocol notes
    protocol/simconnect.md
    runbooks/dev-loop.md, test-flight.md
  mapping/
    simvars.yaml         # MSFS SimVar/event -> X-Plane dataref/command + status
    facilities.yaml
  src/
    ClearanceLink.Protocol/  # wire codec, pure
    ClearanceLink.Server/    # transports, sessions, dispatcher
    ClearanceLink.Mapping/   # mapping engine + unit conversions
    ClearanceLink.XPlane/    # Web API client, FakeXPlane
    ClearanceLink.NavData/   # apt.dat / CIFP parsers, SQLite index
    ClearanceLink.Traffic/   # AI object registry (stub first)
    ClearanceLink.Recorder/  # JSONL record/replay
    ClearanceLink.Host/      # entry point, dashboard, status API
  tests/                 # one test project per src project + Replay.Tests
  recordings/            # sanitized sample sessions used as tests
  tools/                 # simconnect-probe, replay CLI, node-simconnect test client
  plugin/                # (Phase 4) C++ X-Plane plugin with XPMP2
  scripts/               # dev.ps1, test.ps1, publish.ps1
  .claude/skills/        # add-simvar-mapping, analyze-recording, triage-unmapped
  logs/                  # gitignored
```

**Memory, in two layers:**
- **Repo (durable, versioned):** `AGENTS.md` (how to work), `docs/STATUS.md` (where we are), `docs/decisions/` (why), `mapping/*.yaml` (what maps to what). Rule in AGENTS.md: every PR updates STATUS.md and the mapping file it touches.
- **Project memory (this chat):** short pointers only: repo URL, current phase, your preferences, open questions. No duplicated technical detail.

## 8. What I need from you

1. **Do you have MSFS installed on this PC?** If yes, Phase 0 can record a real ATC app session, which removes most guesswork. If no, we still go ahead by logging what the ATC app asks for.
2. A GitHub repo (empty is fine) attached to this project, and your project folder connected with Remote Control so Phase 0 can run on your PC with the ATC app and X-Plane.

## Sources
- The ATC app's own online documentation (FAQ, getting started, toolbar, live traffic, SimConnect troubleshooting), a public listing of its install files, and its public feature-request board (an X-Plane 12 request, no official commitment). Links left out on purpose.
- [node-simconnect](https://github.com/EvenAR/node-simconnect), [jSimConnect](https://github.com/mharj/jsimconnect)
- [MSFS SimConnect Facilities API](https://docs.flightsimulator.com/html/Programming_Tools/SimConnect/API_Reference/Facilities/SimConnect_RequestFacilityData.htm)
- [X-Plane Web API](https://developer.x-plane.com/article/x-plane-web-api/), [12.4.4 release notes](https://www.x-plane.com/kb/x-plane-12-4-4-release-notes/), [12.4.4 SDK coverage](https://fselite.net/content/x-plane-12-4-4-beta-released-with-new-sdk-apis-persistent-snow-and-performance-work/), [SDK 12.4.0](https://developer.x-plane.com/2026/02/sdk-news-12-4-0-fast-and-furious/)
