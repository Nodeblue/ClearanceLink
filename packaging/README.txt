ClearanceLink - Connect BeyondATC to X-Plane 12
===============================================

Free and experimental, by Nodeblue, with no warranty. Not affiliated with, endorsed by or supported by
BeyondATC, Microsoft or Laminar Research. Free software under the GNU GPL version 3 (LICENSE.txt), with
a permission to link with X-Plane (LICENSE-EXCEPTION.txt). Source code: https://github.com/Nodeblue/ClearanceLink.
Parts by others and their licences: THIRD-PARTY-NOTICES.txt.

Full help, with pictures: https://github.com/Nodeblue/ClearanceLink/blob/main/docs/help.md


First start
-----------
Double-click ClearanceLink.exe. Windows may warn that it doesn't know the app: press More info, then
Run anyway. Six WELCOME pages then find BeyondATC and X-Plane, install the X-Plane plugin that draws
BeyondATC's traffic, set up aircraft models and list the X-Plane settings to turn down. Press the button
beside a row, or click the row. On the keyboard: 1-6 for the left buttons, Shift+1-6 for the right ones.
You can open the WELCOME pages again from the SETUP page.

You need Windows 10 or 11 (64-bit), X-Plane 12.1.4 or later and BeyondATC. Nothing else.


Every flight
------------
1. Plan your flight in SimBrief. BeyondATC loads your latest SimBrief plan itself.
2. Start X-Plane 12 and load your aircraft at the SimBrief departure airport.
3. Double-click ClearanceLink.exe and leave its window open.
4. Optional: on the FPLN page press REQUEST to fetch the same SimBrief plan, check it, then press LOAD
   twice to put it in X-Plane's FMS. It is also saved in X-Plane 12\Output\FMS plans for aircraft with
   their own FMS.
5. Start BeyondATC. The STATUS page says READY FOR CLEARANCE when all is connected, or CLOSE MSFS TO
   CONNECT if Microsoft Flight Simulator is open.

If ClearanceLink is closed or restarted during a flight, restart BeyondATC too.


X-Plane settings
----------------
So only BeyondATC talks and flies traffic: in X-Plane's Settings > Sound set the Radios slider to 0, in
Settings > Graphics turn Draw parked aircraft off, and remove the AI aircraft from your flight
(Flight Configuration > AI Aircraft).


Traffic
-------
BeyondATC needs FSLTL for any traffic: without it, it says "No traffic liveries found". Without MSFS 2024,
choose your FSLTL folder on the TRAFFIC page. BeyondATC's traffic is drawn by ClearanceLink's X-Plane plugin
with CSL aircraft models: FSLTL (https://fslivetrafficliveries.com/; if installed for MSFS, press CONVERT on the
TRAFFIC page with X-Plane closed; converted on your PC, never share the result), plus any CSL models such as
Bluebell (free from the X-Plane.org forums: unzip into X-Plane 12\Custom Data\CSL) or X-CSL (free, from
x-csl.ru). CSL models only change how traffic looks; BeyondATC still needs FSLTL. CHECK on the TRAFFIC page
shows what's in your FSLTL folder and whether BeyondATC sees it. Close X-Plane before installing or updating the
plugin from the SETUP page.


Pushback
--------
The PUSH page pushes you back when BeyondATC clears you, like GSX in MSFS. It needs the free Better
Pushback plugin, version 1.14 or later:
https://forums.x-plane.org/files/file/90556-better-pushback-for-x-plane-1112/
Unzip it into X-Plane 12\Resources\plugins with X-Plane closed.


Problems
--------
On the SUPPORT page, press CREATE SUPPORT FILE and send the zip with a few words on what went wrong, at
https://github.com/Nodeblue/ClearanceLink/issues. It holds ClearanceLink's recent logs, BeyondATC's log and
the end of X-Plane's log, with your Windows user name blanked out. More fixes are on the help page.
