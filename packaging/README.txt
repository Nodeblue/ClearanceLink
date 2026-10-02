XPConnect - BeyondATC for X-Plane 12
====================================

XPConnect lets BeyondATC, which is made for Microsoft Flight Simulator, work with X-Plane 12. It stands in
for MSFS: BeyondATC talks to XPConnect, and XPConnect answers from X-Plane (your aircraft, radios and
transponder) and from X-Plane's own airport and procedure data (gates, taxiways, frequencies, SIDs, STARs,
approaches).

This is an independent project, not made or supported by BeyondATC or Laminar Research.
XPConnect is free software under the MIT licence (LICENSE.txt).


What you need
-------------
- Windows 10 or 11 (64-bit).
- X-Plane 12.1.4 or later.
- BeyondATC installed (it can stay installed for MSFS as well).
Nothing else: XPConnect does not need .NET or MSFS to be installed.


Every flight
------------
1. Start X-Plane 12 and load your aircraft.
2. Double-click XPConnect.exe and leave its window open.
   The first time, it asks to switch BeyondATC to X-Plane: answer Y.
   Your browser opens XPConnect's status page (http://localhost:5577/). It shows whether X-Plane and
   BeyondATC are connected and which airports have been sent, and says what to start next.
3. Start BeyondATC and start your flight as usual (your SimBrief plan is loaded by BeyondATC itself).
4. When you have finished, close BeyondATC, then close the XPConnect window.

Start XPConnect before BeyondATC. If XPConnect is closed or restarted during a flight, BeyondATC does not
reconnect on its own: restart BeyondATC.


Going back to MSFS
------------------
Double-click "Use BeyondATC with MSFS.cmd". BeyondATC then connects to MSFS again.
"Use BeyondATC with X-Plane.cmd" switches it back (XPConnect.exe also offers this when it starts).

These only add or remove two small SimConnect.cfg files in BeyondATC's folder; nothing in MSFS, X-Plane or
BeyondATC itself is changed. If you already had a SimConnect.cfg there, it is kept and put back.


X-Plane settings
----------------
- Turn X-Plane's own ATC down to zero (Settings > Sound), or X-Plane's controllers and ATIS talk over
  BeyondATC's.
- XPConnect reads X-Plane through its built-in Web API on port 8086, which is on by default.
- Turn BeyondATC's AI traffic off for now; AI traffic in X-Plane is not supported yet.


What works and what doesn't (yet)
---------------------------------
Works: connection, clearance, taxi routes from X-Plane's taxiways, SIDs and STARs, frequencies and
automatic COM tuning, handoffs, approaches, altitudes and flight levels.
Not yet: BeyondATC's AI traffic, push-to-talk and quick-reply buttons bound inside the sim (use
BeyondATC's own keys), GSX integration.
Airports without taxiways in X-Plane get a simple generated taxi network.


If something goes wrong
-----------------------
- "Flight Simulator is not running!" in BeyondATC: XPConnect is not running. Start it first.
- BeyondATC says it cannot connect: check XPConnect's window says "listening on tcp:127.0.0.1:5112" and,
  once X-Plane is up, "xplane CONNECTED".
- XPConnect cannot find BeyondATC or X-Plane: run in a command prompt (it is remembered)
      XPConnect.exe settings batc-folder "D:\Path\To\BeyondATC"
      XPConnect.exe settings xplane-folder "D:\Path\To\X-Plane 12"
- To report a problem, double-click "Create support file.cmd". It makes one zip of XPConnect's recent session
  logs, the newest recording, BeyondATC's Player.log and the end of X-Plane's Log.txt, with your Windows user
  name blanked out, and shows it in Explorer. Send that zip with a few words on what went wrong.
  Logs are kept in %LOCALAPPDATA%\XPConnect for 7 days. To stop writing them: XPConnect.exe settings session-log off

Commands (for a command prompt):
  XPConnect.exe                 set up if needed, then run (same as double-clicking)
  XPConnect.exe batc status     show whether BeyondATC points at XPConnect or MSFS
  XPConnect.exe batc on | off   switch BeyondATC to X-Plane / MSFS
  XPConnect.exe settings        show or change the folders and log settings XPConnect uses
  XPConnect.exe support         make the support zip
  XPConnect.exe xplane-check    check XPConnect can read everything it needs from a running X-Plane
  XPConnect.exe --help          all commands
