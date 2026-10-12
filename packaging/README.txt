ClearanceLink - an experimental SimConnect to X-Plane 12 bridge
==============================================================

Free and experimental, by Nodeblue, with no warranty: a development proof of concept. It is not made,
supported or endorsed by any developer whose app uses SimConnect, nor by Microsoft or Laminar Research.
Free software under the GNU GPL version 3 (LICENSE.txt), with a permission to link with X-Plane
(LICENSE-EXCEPTION.txt). Source code: https://github.com/Nodeblue/ClearanceLink.
Parts by others and their licences: THIRD-PARTY-NOTICES.txt.

Full help, with pictures: https://github.com/Nodeblue/ClearanceLink/blob/main/docs/help.md


First start
-----------
Double-click ClearanceLink.exe. Windows may warn that it doesn't know the app: press More info, then
Run anyway. On six WELCOME pages you choose your ATC app's .exe (FOLDERS page: CHOOSE beside
ATC APP). ClearanceLink finds X-Plane, installs the X-Plane plugin that draws the app's traffic, sets
up aircraft models and lists the X-Plane settings to turn down. Press the button beside a row, or
click the row. On the keyboard: 1-6 for the left buttons, Shift+1-6 for the right ones.
You can open the WELCOME pages again from the SETUP page.

You need Windows 10 or 11 (64-bit), X-Plane 12.1.4 or later and an ATC app that uses
SimConnect. Nothing else.


Every flight
------------
1. Plan your flight in SimBrief, if your ATC app uses SimBrief.
2. Start X-Plane 12 and load your aircraft at the SimBrief departure airport.
3. Double-click ClearanceLink.exe and leave its window open.
4. Optional: on the FPLN page press REQUEST to fetch the same SimBrief plan, check it, then press LOAD
   twice to put it in X-Plane's FMS. It is also saved in X-Plane 12\Output\FMS plans for aircraft with
   their own FMS.
5. Start your ATC app. The STATUS page says READY FOR CLEARANCE when all is connected, or CLOSE MSFS TO
   CONNECT if Microsoft Flight Simulator is open.

If ClearanceLink is closed or restarted during a flight, restart the ATC app too.


X-Plane settings
----------------
So only the ATC app talks and flies traffic: in X-Plane's Settings > Sound set the Radios slider to 0, in
Settings > Graphics turn Draw parked aircraft off, and remove the AI aircraft from your flight
(Flight Configuration > AI Aircraft).


Traffic
-------
The ATC app flies its own traffic and ClearanceLink's X-Plane plugin draws it. Traffic is optional: ATC
works without it. Everything is on the TRAFFIC page (on SETUP, press SET UP beside TRAFFIC). Close
X-Plane for steps 1 and 3.

1. Press INSTALL beside X-PLANE PLUGIN.
2. Install FSLTL (free, https://fslivetrafficliveries.com/). Your ATC app may need it for traffic.
   Install it for MSFS 2024 if you have it, else for MSFS 2020. No MSFS? Copy the
   fsltl-traffic-base folder from a PC that has FSLTL, then press CHOOSE on the FSLTL line and pick it.
3. Press CONVERT on the FSLTL line and wait until it shows a number of liveries (several minutes,
   about 13 GB). The converted models stay on your PC: FSLTL's licence doesn't allow sharing them.
4. Press CHECK beside ATC APP TRAFFIC. The bottom line says FSLTL READY, or the one thing to fix.

Optional: free CSL models such as Bluebell or X-CSL fill in aircraft FSLTL doesn't have. Put them in
X-Plane 12\Custom Data\CSL. They don't replace FSLTL: the ATC app can't see them.

No traffic? The help page has a checklist under Problems > No traffic.


Pushback
--------
The PUSH page pushes you back when the ATC app clears you, like GSX in MSFS. It needs the free Better
Pushback plugin, version 1.14 or later:
https://forums.x-plane.org/files/file/90556-better-pushback-for-x-plane-1112/
Unzip it into X-Plane 12\Resources\plugins with X-Plane closed.


Problems
--------
On the SUPPORT page, press CREATE SUPPORT FILE and send the zip with a few words on what went wrong, at
https://github.com/Nodeblue/ClearanceLink/issues. It holds ClearanceLink's recent logs, the ATC app's log and
the end of X-Plane's log, with your Windows user name blanked out. More fixes are on the help page.
