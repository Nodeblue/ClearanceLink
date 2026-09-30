# Architecture

```
BATC (Unity) --WebSocket--> simconnect_ws.exe --SimConnect pipe/TCP--> XPConnect --Web API--> X-Plane 12
                                                                          |
                                                                          +--files--> apt.dat, CIFP (Phase 2)
                                                                          +--plugin--> AI traffic (Phase 4)
```

## Components (planned; the ones marked * exist today)
| Component | Role |
|---|---|
| Transport* | Named pipe and TCP listeners; yield plain streams. |
| Protocol* | Packet framing, decode/encode. Pure. |
| Recorder* | JSONL recording and call inventory. Recordings double as replay test fixtures. |
| Session / dispatcher | Per-client state: data definitions, requests and periods, event maps, subscriptions. |
| Mapping | Loads `mapping/*.yaml`; converts SimVar + unit to dataref + conversion. |
| XPlane | Web API client (REST + WebSocket), with a `FakeXPlane` for tests. |
| NavData | Parses X-Plane data into a SQLite index that answers the Facilities API. |
| Traffic | AI object registry. Stubbed first, then plugin-backed. |
| Dashboard | Local web UI with connection status, live SimVars, unmapped calls and a recording control. |

## Principles
- Pure core, thin edges; every external system sits behind an interface with a fake.
- Every request has an explicit policy: implemented, emulated, stubbed with success, or rejected with a SimConnect exception.
- Recording is always on during development. Unmapped calls are counted and reported.
