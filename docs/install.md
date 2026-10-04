# Install and update

ClearanceLink is experimental, and an independent project: it is not made, supported or endorsed by BeyondATC or
Laminar Research.

## Download
1. Open the [latest release](https://github.com/Nodeblue/XPConnect/releases/latest) and download
   `ClearanceLink-<version>-win-x64.zip` (under **Assets**). If your browser says the file isn't commonly downloaded,
   choose **Keep**.
2. **Before unzipping**, right-click the zip, choose **Properties**, tick **Unblock** at the bottom and press **OK**.
   This stops Windows warning about each file inside. (No Unblock box? Nothing to do.)
3. Unzip it to a folder of your own, for example `Documents\ClearanceLink`. Avoid `Program Files`: Windows won't let you
   update files there without admin rights.
4. Double-click `ClearanceLink.exe`.

## "Windows protected your PC"
ClearanceLink isn't signed with a paid certificate, so Windows doesn't know who made it. If you see this blue box,
press **More info**, then **Run anyway**. Windows remembers, so it asks once.

- If there is no **Run anyway** button, Windows 11's *Smart App Control* is on and blocks unsigned apps. ClearanceLink
  can't run while it is on.
- If your antivirus quarantines `ClearanceLink.exe`, restore it and allow it. The whole source code is in this
  repository, and the zip is built from it by GitHub Actions; each release lists the zip's SHA-256 so you can check
  your download (`Get-FileHash ClearanceLink-<version>-win-x64.zip` in PowerShell).

## Knowing about updates
When ClearanceLink starts it asks GitHub whether a newer release exists. If there is one, the **ABOUT** page shows
**NEW VERSION** with its number; press the key beside it to open the download page. ClearanceLink never downloads or
installs anything itself. To stop the check: `ClearanceLink.exe settings update-check off` in a command prompt.

## Updating
1. Close BeyondATC and ClearanceLink.
2. Download the new zip and **Unblock** it as above.
3. Unzip it into your ClearanceLink folder and choose **Replace the files**. Your settings and logs are kept (they are in
   `%LOCALAPPDATA%\ClearanceLink`, not in this folder).
4. Start ClearanceLink. If **SETUP** shows the X-PLANE PLUGIN as **NEWER VERSION**, close X-Plane and press **UPDATE**
   (X-Plane keeps the plugin locked while it runs).

**Coming from XPConnect** (ClearanceLink's old name)? Unzip ClearanceLink into a new folder and delete the old
XPConnect folder. ClearanceLink picks up your settings, and pressing UPDATE on SETUP replaces the old XPConnect plugin.

## Removing ClearanceLink
1. On **SETUP**, if CONNECTION is **ALWAYS ON**, choose **AUTOMATIC** first, so BeyondATC goes back to MSFS.
2. Close ClearanceLink and delete its folder.
3. Optional: delete `X-Plane 12\Resources\plugins\ClearanceLink` (the traffic add-on) and `%LOCALAPPDATA%\ClearanceLink`.
