# ClearanceLink help

ClearanceLink lets [BeyondATC](https://www.beyondatc.net/), which is made for Microsoft Flight Simulator, work with
X-Plane 12. BeyondATC talks to ClearanceLink as if it were MSFS, and ClearanceLink answers from X-Plane.

> **Experimental.** ClearanceLink is a free hobby project by Nodeblue. It is not affiliated with, endorsed by or
> supported by BeyondATC, Microsoft or Laminar Research. Please don't ask BeyondATC's team for help with it.

**Contents:** [What you need](#what-you-need) · [First start](#first-start) · [Every flight](#every-flight) ·
[Aircraft models for traffic](#aircraft-models-for-traffic) · [X-Plane settings](#x-plane-settings) ·
[BeyondATC settings](#beyondatc-settings) · [Flying MSFS as well](#flying-msfs-as-well) ·
[What works](#what-works) · [Problems](#problems) · [Commands](#commands)

## What you need
- Windows 10 or 11 (64-bit).
- X-Plane 12.1.4 or later.
- BeyondATC, installed as usual. It stays set up for MSFS too.
- For traffic in X-Plane (optional): Bluebell CSL models or an FSLTL install in MSFS. See
  [Aircraft models for traffic](#aircraft-models-for-traffic).

You don't need .NET or MSFS installed.

## First start
1. Unzip ClearanceLink anywhere you like, for example `Documents\ClearanceLink`.
2. Double-click `ClearanceLink.exe`. ClearanceLink isn't signed with a paid certificate yet, so Windows may warn you the
   first time. For downloading, updates and that warning, see [Download and install](install.md).
3. The window opens on six **WELCOME** pages. Use the buttons beside each line, or keys 1-6 (left) and Shift+1-6
   (right). After the welcome message:
   - **Folders.** ClearanceLink finds BeyondATC and X-Plane by itself. If a line says NOT FOUND, press CHOOSE and pick
     the folder (the BeyondATC folder is the one with `BeyondATC.exe`). Nothing in these folders is changed.
   - **BeyondATC's traffic.** BeyondATC creates and flies its own traffic; ClearanceLink's X-Plane plugin draws it in
     X-Plane. Press INSTALL (close X-Plane first). Traffic is optional: skip it and ATC still works.
   - **Aircraft models.** The plugin draws traffic with Bluebell or FSLTL models, see
     [Aircraft models for traffic](#aircraft-models-for-traffic).
   - **X-Plane settings.** Three settings to turn down, explained [below](#x-plane-settings).
   - **Every flight.** The order to start things in, below.

You can open the welcome pages again from the SUPPORT page.

## Every flight
1. Plan your flight in [SimBrief](https://dispatch.simbrief.com/) and generate the flight plan (OFP). BeyondATC
   loads your latest SimBrief plan itself, the same as with MSFS; X-Plane's own FMS plan isn't used.
2. Start X-Plane 12 and load your aircraft at the SimBrief departure airport, at a gate or parking spot.
3. Double-click `ClearanceLink.exe` and leave its window open.
4. Start BeyondATC and start your flight as usual.

The bottom line of the STATUS page says what to do next, and shows **READY FOR CLEARANCE** when everything is
connected. If MSFS is open it says CLOSE MSFS TO CONNECT (see [Flying MSFS as well](#flying-msfs-as-well)).
When you've finished, close BeyondATC, then ClearanceLink.

Start ClearanceLink before BeyondATC. If you close or restart ClearanceLink during a flight, restart BeyondATC too: it
doesn't reconnect by itself.

## Aircraft models for traffic
BeyondATC's traffic (aircraft at gates, taxiing, departing and arriving) appears in X-Plane through ClearanceLink's
X-Plane plugin, which draws each aircraft with a CSL model. X-Plane doesn't come with CSL models. Two libraries are
tested and supported; either one is enough, and you can have both.

### Bluebell
1. Download the **Bluebell OBJ8 CSL** packages (free, from the X-Plane.org forums; search for "Bluebell CSL").
2. Unzip them into `X-Plane 12\Custom Data\CSL`, so you end up with folders like
   `X-Plane 12\Custom Data\CSL\BB_Airbus`. A folder that holds the packages one level down also works.
3. Welcome page 4 (or SETUP) shows how many packages ClearanceLink found.

### FSLTL
If you have FSLTL (FS Live Traffic Liveries) installed in MSFS, ClearanceLink can convert its aircraft for X-Plane on
your PC. The converted models stay on your PC and are never shared.
1. On welcome page 4, the FSLTL line says FOUND IN MSFS when ClearanceLink finds `fsltl-traffic-base` in MSFS's
   Community folder (through MSFS's own settings). If it says NOT FOUND, press CHOOSE and pick the
   `fsltl-traffic-base` folder.
2. Close X-Plane and press CONVERT. It runs in the background and can take a while; LIVE LOG on SUPPORT shows
   progress. When it's done the line shows how many liveries were converted.
3. The models go to `X-Plane 12\Custom Data\CSL\ClearanceLink-FSLTL`. After FSLTL updates, press UPDATE to convert
   again.

### Other models
The plugin also uses models already installed for LiveTraffic, xPilot, X-IvAp or swift. Each BeyondATC aircraft is
drawn with the closest model and livery available; an airline without a matching livery gets another livery of the
same type.

The plugin lives in `X-Plane 12\Resources\plugins\ClearanceLink`. X-Plane locks it while running, so close X-Plane
before installing or updating it from the SETUP page.

## X-Plane settings
Change these in X-Plane, so only BeyondATC talks and flies traffic:

| Where | Setting | Why |
|---|---|---|
| Settings > Sound | **Radios** slider to 0 | Otherwise X-Plane's own controllers and ATIS talk over BeyondATC's. |
| Settings > Graphics | **Draw parked aircraft** unticked | X-Plane parks aircraft at gates where BeyondATC parks its traffic. |
| Your flight's set-up | No AI aircraft | X-Plane's AI flies its own aircraft, which BeyondATC doesn't know about. |

ClearanceLink reads X-Plane through X-Plane's built-in Web API on port 8086, which is on by default.

## BeyondATC settings
Nothing needs changing. BeyondATC sees ClearanceLink as "MSFS 2024".
- Your SimBrief account is set in BeyondATC, as for MSFS. BeyondATC loads your latest SimBrief flight plan itself
  and takes only your position from X-Plane, so the plan's departure airport must be the one you're parked at.
  A plan without a SID in its route gets cleared with "radar vectors".
- If BeyondATC's traffic is on, it appears in X-Plane once you've installed the
  [models](#aircraft-models-for-traffic). Without models it still talks on frequency, but you won't see it.

## Flying MSFS as well
Nothing to switch. ClearanceLink answers BeyondATC on the connection MSFS uses, but only while MSFS is closed. Close
ClearanceLink and start MSFS, and BeyondATC talks to MSFS as usual. If MSFS starts while ClearanceLink is running,
ClearanceLink steps aside.

If you keep MSFS open while flying X-Plane, choose **ALWAYS ON** on the SETUP page. That adds two small config
files to BeyondATC's folder so it always goes to ClearanceLink; choosing **AUTOMATIC** removes them again.

## What works
- Clearance, taxi routes from X-Plane's own taxiways, SIDs and STARs, frequencies and automatic COM tuning,
  handoffs, approaches, altitudes and flight levels.
- BeyondATC's traffic, drawn in X-Plane with CSL models.
- Airports without taxiways in X-Plane get a simple generated taxi network.

Not yet: push-to-talk and quick-reply buttons bound inside the sim (use BeyondATC's own keys), and GSX.

## Problems
| What you see | What to do |
|---|---|
| BeyondATC says "Flight Simulator is not running!" | Start ClearanceLink first, then BeyondATC. |
| STATUS says CLOSE MSFS TO CONNECT | Close MSFS, or choose ALWAYS ON on SETUP. |
| STATUS says START X-PLANE 12 with X-Plane open | Load an aircraft; X-Plane answers once a flight is loaded. |
| A folder says NOT FOUND | On SETUP (or welcome page 2), press CHOOSE beside it and pick the folder. |
| No traffic visible | Check SETUP shows the X-Plane plugin INSTALLED and welcome page 4 finds Bluebell packages or converted FSLTL liveries. |
| X-Plane talks over BeyondATC | Set the Radios slider to 0 (X-Plane Settings > Sound). |

**Reporting a problem:** on the SUPPORT page press **CREATE SUPPORT FILE**. It makes one zip of ClearanceLink's recent
logs, BeyondATC's log and the end of X-Plane's log, with your Windows user name blanked out, and shows it in
Explorer. Send it with a few words on what went wrong, on the
[ClearanceLink GitHub page](https://github.com/Nodeblue/ClearanceLink/issues).

Logs stay in `%LOCALAPPDATA%\ClearanceLink` for 7 days. You can turn the session log off on SUPPORT, and LIVE LOG
shows what ClearanceLink is doing right now.

## Releases and updates
Downloading, the Windows warning, update notices and updating: see [Download and install](install.md).

## Commands
For a command prompt in ClearanceLink's folder:

```
ClearanceLink.exe start          run in the command prompt instead of the window
ClearanceLink.exe settings       show or change the folders, connection and log settings
ClearanceLink.exe support        make the support zip
ClearanceLink.exe plugin status  is the X-Plane plugin installed? (plugin install | remove)
ClearanceLink.exe xplane-check   check ClearanceLink can read what it needs from a running X-Plane
ClearanceLink.exe --help         all commands
```
