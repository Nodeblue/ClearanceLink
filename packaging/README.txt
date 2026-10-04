ClearanceLink - Connect BeyondATC to X-Plane 12
===============================================

Experimental hobby project by Nodeblue. Not affiliated with, endorsed by or supported by BeyondATC,
Microsoft or Laminar Research. Free software under the GNU GPL version 3 (LICENSE.txt), with a permission
to link with X-Plane (LICENSE-EXCEPTION.txt). Source code: https://github.com/Nodeblue/ClearanceLink.
Parts by others and their licences: THIRD-PARTY-NOTICES.txt.

Full help: https://github.com/Nodeblue/ClearanceLink/blob/main/docs/help.md


First start
-----------
Double-click ClearanceLink.exe. The WELCOME pages find BeyondATC and X-Plane, install the X-Plane
plugin that draws BeyondATC's traffic, set up aircraft models and list the X-Plane settings to turn down. Use the buttons beside each line, or keys 1-6 (left)
and Shift+1-6 (right). You can open them again from the SUPPORT page.

You need Windows 10/11 (64-bit), X-Plane 12.1.4 or later and BeyondATC. Nothing else.


Every flight
------------
1. Plan your flight in SimBrief. BeyondATC loads your latest SimBrief plan itself.
2. Start X-Plane 12 and load your aircraft at the SimBrief departure airport.
3. Double-click ClearanceLink.exe and leave its window open.
4. Start BeyondATC. The STATUS page says READY FOR CLEARANCE when all is connected, or CLOSE MSFS TO
   CONNECT if Microsoft Flight Simulator is open.

If ClearanceLink is closed or restarted during a flight, restart BeyondATC too.


Traffic
-------
BeyondATC's traffic is drawn by ClearanceLink's X-Plane plugin with CSL aircraft models. Two libraries are
tested: Bluebell (free from the X-Plane.org forums, unzip into X-Plane 12\Custom Data\CSL) and FSLTL
(if installed in MSFS, press CONVERT on welcome page 4 with X-Plane closed; converted on your PC).
Close X-Plane before installing or updating the plugin from the SETUP page.


X-Plane settings
----------------
So only BeyondATC talks and flies traffic: in X-Plane's Settings > Sound set the Radios slider to 0, in
Settings > Graphics untick Draw parked aircraft, and don't add AI aircraft to your flight.


Problems
--------
On the SUPPORT page, press CREATE SUPPORT FILE and send the zip with a few words on what went wrong.
It holds ClearanceLink's recent logs, BeyondATC's log and the end of X-Plane's log, with your Windows user
name blanked out. More fixes are on the help page.
