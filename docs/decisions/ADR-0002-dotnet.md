# ADR-0002: C# on .NET 10

**Status:** accepted. **Date:** 2026-09-30.

## Decision
The middleware is C# / .NET 10 (LTS). A later X-Plane plugin, needed only for AI traffic, will be C++ with XPMP2.

## Why
- First-class Windows named pipes and binary marshaling that map directly onto `SimConnect.h`.
- Builds and tests on Linux, so cloud agents can work without the Windows PC.
- Fast loop: `dotnet watch`, and a sub-minute test suite. Single-file publish produces one exe.
- Strong typing catches agent mistakes at compile time.

## Alternatives
TypeScript is weaker for a long-running binary server. Rust iterates more slowly for agents. Both remain fine for tools; `tools/node-client` uses node-simconnect as an independent test client.
