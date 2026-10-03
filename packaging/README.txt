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
1. Close Microsoft Flight Simulator if it is open. Start X-Plane 12 and load your aircraft.
2. Double-click XPConnect.exe and leave its window open. Its STATUS page shows whether X-Plane and
   BeyondATC are connected, and the bottom line says what to do next ("READY FOR CLEARANCE" when all is well).
   Use the buttons beside each line (or keys 1-6, Shift+1-6 for the right side, F1-F4 for the pages).
3. Start BeyondATC and start your flight as usual (your SimBrief plan is loaded by BeyondATC itself).
4. When you have finished, close BeyondATC, then close the XPConnect window.

Start XPConnect before BeyondATC. If XPConnect is closed or restarted during a flight, BeyondATC does not
reconnect on its own: restart BeyondATC.


Using BeyondATC with MSFS as well
---------------------------------
Nothing to switch. XPConnect answers BeyondATC on the same connection MSFS uses, but only while MSFS is
closed and XPConnect is running. Close XPConnect and start MSFS, and BeyondATC talks to MSFS as usual.
If MSFS starts while XPConnect is running, XPConnect steps aside.

Nothing in MSFS, X-Plane or BeyondATC is changed. If you used XPConnect 0.3.0, it put two SimConnect.cfg files
in BeyondATC's folder; the SETUP page offers to remove them.

If you keep MSFS open while flying X-Plane, choose ALWAYS ON on the SETUP page. That writes those two config
files so BeyondATC always goes to XPConnect; choosing AUTOMATIC removes them again.


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
- BeyondATC says it cannot connect: make sure MSFS is closed, and check XPConnect's window says "listening on"
  and, once X-Plane is up, "xplane CONNECTED".
- XPConnect cannot find BeyondATC or X-Plane: on the SETUP page, press the button beside the folder and choose it.
  It is remembered.
- To report a problem, press CREATE SUPPORT FILE on the SUPPORT page. It makes one zip of XPConnect's recent
  session logs, the newest recording, BeyondATC's Player.log and the end of X-Plane's Log.txt, with your Windows
  user name blanked out, and shows it in Explorer. Send that zip with a few words on what went wrong.
  Logs are kept in %LOCALAPPDATA%\XPConnect for 7 days; the session log can be turned off on the same page.
- SHOW LIVE LOG on the SUPPORT page shows everything XPConnect is doing; the web page on SETUP has more detail.

Commands (for a command prompt):
  XPConnect.exe start           run in the command prompt instead of the window
  XPConnect.exe settings connection automatic | always-on
                                automatic (default): nothing changed in BeyondATC, close MSFS before flying
                                always-on: BeyondATC's config files point at XPConnect even while MSFS is open
  XPConnect.exe settings        show or change the folders and log settings XPConnect uses
  XPConnect.exe support         make the support zip
  XPConnect.exe xplane-check    check XPConnect can read everything it needs from a running X-Plane
  XPConnect.exe --help          all commands
