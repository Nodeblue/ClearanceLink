XPConnect - use BeyondATC with X-Plane 12
=========================================

Experimental hobby project by Nodeblue. Not affiliated with, endorsed by or supported by BeyondATC,
Microsoft or Laminar Research. Free software under the MIT licence (LICENSE.txt).

Full help: https://github.com/Nodeblue/XPConnect/blob/main/docs/help.md


First start
-----------
Double-click XPConnect.exe. The WELCOME pages find BeyondATC and X-Plane, install the traffic
add-on and list the X-Plane settings to turn down. Use the buttons beside each line, or keys 1-6 (left)
and Shift+1-6 (right). You can open them again from the SUPPORT page.

You need Windows 10/11 (64-bit), X-Plane 12.1.4 or later and BeyondATC. Nothing else.


Every flight
------------
1. Close Microsoft Flight Simulator if it is open.
2. Start X-Plane 12 and load your aircraft.
3. Double-click XPConnect.exe and leave its window open.
4. Start BeyondATC. The STATUS page says READY FOR CLEARANCE when all is connected.

If XPConnect is closed or restarted during a flight, restart BeyondATC too.


Traffic
-------
BeyondATC's traffic is drawn with CSL aircraft models. Download the free Bluebell OBJ8 CSL packages
(X-Plane.org forums) and unzip them into X-Plane 12\Custom Data\CSL. Close X-Plane before installing
or updating the traffic add-on from the SETUP page.


X-Plane settings
----------------
Turn X-Plane's ATC volume to 0 (Settings > Sound), and turn off X-Plane's AI aircraft and parked
(static) aircraft, so only BeyondATC talks and flies traffic.


Problems
--------
On the SUPPORT page, press CREATE SUPPORT FILE and send the zip with a few words on what went wrong.
It holds XPConnect's recent logs, BeyondATC's log and the end of X-Plane's log, with your Windows user
name blanked out. More fixes are on the help page.
