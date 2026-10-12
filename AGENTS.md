# AGENTS.md

This is the single source of instructions for coding agents (Codex, Claude Code) working on ClearanceLink. `CLAUDE.md` only imports this file.

## What this is
ClearanceLink is an experimental SimConnect to X-Plane 12 bridge, a development proof of concept. It lets an MSFS-only ATC add-on ("the ATC app", chosen by the user as its .exe; nothing is detected automatically) run against X-Plane 12. It is not supported or endorsed by any developer whose app uses SimConnect, nor by Microsoft or Laminar Research; never imply which app it is for. The ATC app speaks SimConnect (the app's .exe itself; older app versions also through a helper program). ClearanceLink acts as the SimConnect **server**, standing in for MSFS, and answers from X-Plane:
- Aircraft state, radios and events come from the X-Plane Web API (REST + WebSocket, `localhost:8086`, X-Plane 12.1.1+).
- Airports, taxiways, gates and procedures come from X-Plane's data files (`apt.dat`, CIFP). This is planned for Phase 2.
- AI traffic is a stretch goal and needs a C++ X-Plane plugin (XPMP2) later.

Read before any substantial change: `docs/STATUS.md` (where we are), `docs/architecture.md` (how it fits), `docs/decisions/` (why).

## Build and test
- .NET 10 SDK (`global.json`). Warnings are errors.
- `dotnet build`, then `dotnet test`. Everything runs on Linux and Windows without a simulator.
- Windows wrappers: `scripts/test.ps1`, `scripts/run.ps1 <command>`, `scripts/dev.ps1` (watch mode).
- Independent client check: `cd tools/node-client && npm install && node smoke.js 127.0.0.1 <port>` against `ClearanceLink fake --listen tcp:<port>`.

## Layout
| Path | What |
|---|---|
| `src/ClearanceLink.Protocol` | SimConnect wire format: framing, names, decoders, builders. Pure, no I/O besides streams. |
| `src/ClearanceLink.Recorder` | JSONL recording of every packet + `CallInventory` summary. |
| `src/ClearanceLink.Host` | `ClearanceLink.exe`: the FMS-style window (`Panel/`, Avalonia; pages from `CduPages.Build`) and the CLI (`serve`, `record`, `fake`, `settings`, `support`...). Transports (named pipe, TCP). |
| `tests/*` | xUnit tests, one project per src project. |
| `mapping/` | MSFS SimVar/event to X-Plane mapping tables (data, not code). |
| `recordings/samples/` | Sanitised recordings used as test fixtures. `recordings/local/` is gitignored scratch. |
| `tools/node-client/` | node-simconnect smoke client (independent implementation of the client side). |
| `docs/` | STATUS, architecture, ADRs, runbooks, research, protocol notes. |

## Rules
1. **Record before guessing.** If you are not sure what the ATC app sends or expects, find it in a recording (`recordings/`), or ask for one. Do not invent SimConnect behaviour. Protocol facts go in `docs/protocol/simconnect.md` with their source.
2. **Unknown requests never crash the server.** Decode what you can; log and record the rest.
3. **Mapping is data.** A new SimVar or event mapping is a `mapping/*.yaml` change plus a test, not a new code path.
4. **Tests with every change.** Protocol changes need byte-level tests. Server behaviour needs an in-process client/server test over TCP (see `tests/ClearanceLink.Host.Tests/ServerTests.cs`).
5. **Keep STATUS.md true.** Every PR that changes behaviour updates `docs/STATUS.md`. Decisions with alternatives get an ADR in `docs/decisions/`.
6. **No MSFS or ATC app binaries in the repo.** Do not commit `SimConnect.dll` or anything from the ATC app's folder. node-simconnect and jSimConnect (LGPL) are references only: read them, don't copy code.
7. **Logs are for agents.** Log lines are single-line and greppable, and include the connection number and call name.

## Environment notes
- Ben's PC runs Windows with MSFS 2024, the ATC app and X-Plane 12. Anything that touches those runs there, through Remote Control. Cloud sessions can build and test everything else.
- The ATC app's own log (a Unity app) is `%USERPROFILE%\AppData\LocalLow\<company>\<product>\Player.log`, with company and product from the app's `<Name>_Data\app.info`. Read it alongside our recordings when something fails. The support zip holds it under `app/`.
- The default MSFS SimConnect pipe is `\\.\pipe\Microsoft Flight Simulator\SimConnect`. Only one process can own it, so close MSFS before running `fake` on it.
