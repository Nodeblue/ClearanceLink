# ClearanceLink

**Connect [BeyondATC](https://www.beyondatc.net/) to X-Plane 12.** BeyondATC is made for Microsoft Flight Simulator.
ClearanceLink stands in for MSFS and answers from X-Plane, so you get BeyondATC's clearances, taxi routes, SIDs, STARs,
approaches, handoffs and traffic in X-Plane 12.

> Free, experimental hobby project by Nodeblue, with no warranty. Not affiliated with, endorsed by or supported by
> BeyondATC, Microsoft or Laminar Research. Please don't ask BeyondATC's team for help with it.

<img src="docs/images/panel-status-flying.png" width="330" alt="ClearanceLink's STATUS page: X-Plane and BeyondATC connected, ready for clearance">

## Get started
1. **[Download and install](docs/install.md)**: unzip, double-click `ClearanceLink.exe`. Its WELCOME pages find
   BeyondATC and X-Plane, install the traffic plugin and show the X-Plane settings to turn down.
2. **For traffic, add [FSLTL](https://fslivetrafficliveries.com/)** (free): BeyondATC needs it for any traffic, and
   ClearanceLink converts its aircraft for X-Plane on the TRAFFIC page. CSL models such as Bluebell or X-CSL (free)
   add more aircraft, but don't replace FSLTL.
   See [aircraft models](docs/help.md#aircraft-models-for-traffic).
3. **Every flight**: plan it in SimBrief, start X-Plane 12 at the plan's departure airport, keep ClearanceLink open,
   then start BeyondATC.
4. **Optional, for pushback**: install the free [Better Pushback](https://forums.x-plane.org/files/file/90556-better-pushback-for-x-plane-1112/) plugin (1.14 or later). The PUSH page then
   pushes you back when BeyondATC clears you, like GSX in MSFS.

You need Windows 10 or 11, X-Plane 12.1.4 or later and BeyondATC. MSFS doesn't need to be installed.

**[Help](docs/help.md)**: first start, every flight, X-Plane settings, aircraft models, flying MSFS as well, problems.

## For developers
C# on .NET 10 plus a small C++ X-Plane plugin. `./scripts/test.ps1` builds and tests, `./scripts/run.ps1 serve` runs
from source, `./scripts/publish.ps1` makes the zip. Where things stand: [docs/STATUS.md](docs/STATUS.md). How it fits
together: [docs/architecture.md](docs/architecture.md). Coding agents: [AGENTS.md](AGENTS.md).

## Licence
GPLv3, see [LICENSE](LICENSE), with an [added permission](LICENSE-EXCEPTION.txt) to link the X-Plane plugin with
X-Plane and its SDK. Libraries, the font and the X-Plane SDK parts in the release are under their own licences, listed
in [THIRD-PARTY-NOTICES.txt](packaging/THIRD-PARTY-NOTICES.txt). Test data under other terms
([X-Plane Scenery Gateway excerpts](tests/ClearanceLink.Airports.Tests/fixtures/README.md),
[MSFS recordings](recordings/samples/README.md)) is not part of the release.
