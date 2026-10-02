@echo off
rem BeyondATC goes to XPConnect (X-Plane) even while MSFS is open, by writing two small config files in its folder.
rem Only needed if you keep MSFS running while flying X-Plane. Undo with "Use BeyondATC with MSFS.cmd".
"%~dp0XPConnect.exe" settings connection always-on
pause
