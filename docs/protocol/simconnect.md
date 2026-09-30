# SimConnect wire protocol notes

Facts here must cite a source: a recording in `recordings/`, the MSFS SDK docs, or a reference implementation (node-simconnect `src/SimConnectSocket.ts`, `src/SimConnectPacketBuilder.ts`).

## Framing (node-simconnect, verified against it in tests)
All little-endian.
- Client to server: `u32 size | u32 protocol | u32 0xF0000000|functionId | u32 sendId | payload`
- Server to client: `u32 size | u32 protocol | u32 recvId | payload`
- `size` includes the header.
- Protocol versions: 2 FSX RTM, 3 FSX SP1, 4 FSX SP2, 5 KittyHawk (MSFS 2020), 6 SunRise (MSFS 2024).

## Transports
- Default pipe: `\\.\pipe\Microsoft Flight Simulator\SimConnect`. A client without `SimConnect.cfg` connects there.
- TCP: configured in the sim's `SimConnect.xml`. Clients find it through `SimConnect.cfg` (`[SimConnect] Protocol=IPv4 Address=127.0.0.1 Port=NNNN`).

## Open handshake
Client payload: `char[256] appName | u32 0 | u8 0 | char[3] alias ("HK" KittyHawk, "RS" SunRise, "XSF" FSX) | u32 major | u32 minor | u32 buildMajor | u32 buildMinor`.
Server reply `RecvId.Open (2)`: `char[256] applicationName | u32 x 8 versions (app then SimConnect) | u32 reserved1 | u32 reserved2`.
The real MSFS 2024 values are still to be recorded (Phase 0).

## Function ids
See `src/XPConnect.Protocol/FunctionNames.cs` (0x01 Open ... 0x6C CallCommBusEvent). 0x02 and 0x03 are not identified yet.
