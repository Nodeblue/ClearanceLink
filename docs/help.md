# XPConnect help

XPConnect lets [BeyondATC](https://www.beyondatc.net/), which is made for Microsoft Flight Simulator, work with
X-Plane 12. BeyondATC talks to XPConnect as if it were MSFS, and XPConnect answers from X-Plane.

> **Experimental.** XPConnect is a free hobby project by Nodeblue. It is not affiliated with, endorsed by or
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
1. Unzip XPConnect anywhere you like, for example `Documents\XPConnect`.
2. Double-click `XPConnect.exe`. XPConnect isn't signed with a paid certificate yet, so Windows may warn you the
   first time. For downloading, updates and that warning, see [Releases and updates](#releases-and-updates).
3. The window opens on six **WELCOME** pages. Use the buttons beside each line, or keys 1-6 (left) and Shift+1-6
   (right). After the welcome message:
   - **Folders.** XPConnect finds BeyondATC and X-Plane by itself. If a line says NOT FOUND, press CHOOSE and pick
     the folder (the BeyondATC folder is the one with `BeyondATC.exe`). Nothing in these folders is changed.
   - **BeyondATC's traffic.** BeyondATC creates and flies its own traffic; XPConnect's X-Plane plugin draws it in
     X-Plane. Press INSTALL (close X-Plane first). Traffic is optional: skip it and ATC still works.
   - **Aircraft models.** The plugin draws traffic with Bluebell or FSLTL models, see
     [Aircraft models for traffic](#aircraft-models-for-traffic).
   - **X-Plane settings.** Three settings to turn down, explained [below](#x-plane-settings).
   - **Every flight.** The order to start things in, below.

You can open the welcome pages again from the SUPPORT page.

## Every flight
1. Close Microsoft Flight Simulator if it's open.
2. Start X-Plane 12 and load your aircraft.
3. Double-click `XPConnect.exe` and leave its window open.
4. Start BeyondATC and start your flight as usual.

The bottom line of the STATUS page says what to do next, and shows **READY FOR CLEARANCE** when everything is
connected. When you've finished, close BeyondATC, then XPConnect.

Start XPConnect before BeyondATC. If you close or restart XPConnect during a flight, restart BeyondATC too: it
doesn't reconnect by itself.

## Aircraft models for traffic
BeyondATC's traffic (aircraft at gates, taxiing, departing and arriving) appears in X-Plane through XPConnect's
X-Plane plugin, which draws each aircraft with a CSL model. X-Plane doesn't come with CSL models. Two libraries are
tested and supported; either one is enough, and you can have both.

### Bluebell
1. Download the **Bluebell OBJ8 CSL** packages (free, from the X-Plane.org forums; search for "Bluebell CSL").
2. Unzip them into `X-Plane 12\Custom Data\CSL`, so you end up with folders like
   `X-Plane 12\Custom Data\CSL\BB_Airbus`. A folder that holds the packages one level down also works.
3. Welcome page 4 (or SETUP) shows how many packages XPConnect found.

### FSLTL
If you have FSLTL (FS Live Traffic Liveries) installed in MSFS, XPConnect can convert its aircraft for X-Plane on
your PC. The converted models stay on your PC and are never shared.
1. On welcome page 4, the FSLTL line says FOUND IN MSFS when XPConnect finds `fsltl-traffic-base` in MSFS's
   Community folder (through MSFS's own settings). If it says NOT FOUND, press CHOOSE and pick the
   `fsltl-traffic-base` folder.
2. Close X-Plane and press CONVERT. It runs in the background and can take a while; LIVE LOG on SUPPORT shows
   progress. When it's done the line shows how many liveries were converted.
3. The models go to `X-Plane 12\Custom Data\CSL\XPConnect-FSLTL`. After FSLTL updates, press UPDATE to convert
   again.

### Other models
The plugin also uses models already installed for LiveTraffic, xPilot, X-IvAp or swift. Each BeyondATC aircraft is
drawn with the closest model and livery available; an airline without a matching livery gets another livery of the
same type.

The plugin lives in `X-Plane 12\Resources\plugins\XPConnect`. X-Plane locks it while running, so close X-Plane
before installing or updating it from the SETUP page.

## X-Plane settings
Turn these down in X-Plane's settings, so only BeyondATC talks and flies traffic:

| Setting | Set to | Why |
|---|---|---|
| ATC volume (Settings > Sound) | 0 | Otherwise X-Plane's own controllers and ATIS talk over BeyondATC's. |
| AI aircraft | Off | X-Plane's AI flies its own aircraft, which BeyondATC doesn't know about. |
| Parked (static) aircraft | Off | X-Plane parks aircraft at gates where BeyondATC parks its traffic. |

XPConnect reads X-Plane through X-Plane's built-in Web API on port 8086, which is on by default.

## BeyondATC settings
Nothing needs changing. BeyondATC sees XPConnect as "MSFS 2024".
- BeyondATC loads your latest SimBrief flight plan itself and takes only your position from X-Plane, so the
  plan's departure airport must be the one you're parked at.
- If BeyondATC's traffic is on, it appears in X-Plane once you've installed the
  [models](#aircraft-models-for-traffic). Without models it still talks on frequency, but you won't see it.

## Flying MSFS as well
Nothing to switch. XPConnect answers BeyondATC on the connection MSFS uses, but only while MSFS is closed. Close
XPConnect and start MSFS, and BeyondATC talks to MSFS as usual. If MSFS starts while XPConnect is running,
XPConnect steps aside.

If you keep MSFS open while flying X-Plane, choose **ALWAYS ON** on the SETUP page. That adds two small config
files to BeyondATC's folder so it always goes to XPConnect; choosing **AUTOMATIC** removes them again.

## What works
- Clearance, taxi routes from X-Plane's own taxiways, SIDs and STARs, frequencies and automatic COM tuning,
  handoffs, approaches, altitudes and flight levels.
- BeyondATC's traffic, drawn in X-Plane with CSL models.
- Airports without taxiways in X-Plane get a simple generated taxi network.

Not yet: push-to-talk and quick-reply buttons bound inside the sim (use BeyondATC's own keys), and GSX.

## Problems
| What you see | What to do |
|---|---|
| BeyondATC says "Flight Simulator is not running!" | Start XPConnect first, then BeyondATC. |
| STATUS says CLOSE MSFS TO CONNECT | Close MSFS, or choose ALWAYS ON on SETUP. |
| STATUS says START X-PLANE 12 with X-Plane open | Load an aircraft; X-Plane answers once a flight is loaded. |
| A folder says NOT FOUND | On SETUP (or welcome page 2), press CHOOSE beside it and pick the folder. |
| No traffic visible | Check SETUP shows the X-Plane plugin INSTALLED and welcome page 4 finds Bluebell packages or converted FSLTL liveries. |
| X-Plane talks over BeyondATC | Set X-Plane's ATC volume to 0 (Settings > Sound). |

**Reporting a problem:** on the SUPPORT page press **CREATE SUPPORT FILE**. It makes one zip of XPConnect's recent
logs, BeyondATC's log and the end of X-Plane's log, with your Windows user name blanked out, and shows it in
Explorer. Send it with a few words on what went wrong, on the
[XPConnect GitHub page](https://github.com/Nodeblue/XPConnect/issues).

Logs stay in `%LOCALAPPDATA%\XPConnect` for 7 days. You can turn the session log off on SUPPORT, and LIVE LOG
shows what XPConnect is doing right now.

## Releases and updates
See the [Releases page](https://github.com/Nodeblue/XPConnect/releases).

## Commands
For a command prompt in XPConnect's folder:

```
XPConnect.exe start          run in the command prompt instead of the window
XPConnect.exe settings       show or change the folders, connection and log settings
XPConnect.exe support        make the support zip
XPConnect.exe plugin status  is the X-Plane plugin installed? (plugin install | remove)
XPConnect.exe xplane-check   check XPConnect can read what it needs from a running X-Plane
XPConnect.exe --help         all commands
```
