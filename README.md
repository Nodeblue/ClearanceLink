# ClearanceLink

**Connect [BeyondATC](https://www.beyondatc.net/) to X-Plane 12.** BeyondATC is made for MSFS; ClearanceLink stands in for
MSFS and answers from X-Plane, so you get BeyondATC's clearances, taxi routes, SIDs, STARs, approaches and traffic
in X-Plane.

> Experimental hobby project by Nodeblue. Not affiliated with, endorsed by or supported by BeyondATC.

## Get started
1. Download the latest zip and unzip it anywhere: see [Download and install](docs/install.md).
2. Double-click `ClearanceLink.exe`. Its **WELCOME** pages find BeyondATC and X-Plane and set up traffic.
3. Every flight: plan it in SimBrief, start X-Plane 12 at your SimBrief departure airport, keep
   ClearanceLink open, then start BeyondATC.

You need Windows 10/11, X-Plane 12.1.4+ and BeyondATC. For traffic, add
[aircraft models](docs/help.md#aircraft-models-for-traffic): Bluebell, or FSLTL converted from MSFS.

**[Help page](docs/help.md)**: X-Plane settings, flying MSFS as well, problems and support files.

## For developers
.NET 10, C#. `./scripts/test.ps1` builds and tests, `./scripts/run.ps1 serve` runs from source,
`./scripts/publish.ps1` makes the zip. Where things stand: [docs/STATUS.md](docs/STATUS.md). How it fits together:
[docs/architecture.md](docs/architecture.md). Coding agents: [AGENTS.md](AGENTS.md).

## Licence
GPLv3, see [LICENSE](LICENSE), with an [added permission](LICENSE-EXCEPTION.txt) to link the X-Plane plugin
with X-Plane and its SDK. Libraries, the font and the X-Plane SDK parts in the release are under their own
licences, listed in [THIRD-PARTY-NOTICES.txt](packaging/THIRD-PARTY-NOTICES.txt). Test data under other terms ([X-Plane Scenery Gateway excerpts](tests/ClearanceLink.Airports.Tests/fixtures/README.md),
[MSFS recordings](recordings/samples/README.md)) is not part of the release.
