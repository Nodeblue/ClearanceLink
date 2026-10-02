@echo off
rem Back to the automatic connection: removes XPConnect's config files from BeyondATC's folder, so BeyondATC
rem connects to MSFS whenever XPConnect is closed, and to X-Plane through XPConnect when MSFS is closed.
"%~dp0XPConnect.exe" settings connection automatic
pause
