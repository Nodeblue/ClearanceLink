# ADR-0007: GitHub releases, and an update check that only looks

Date: 2026-10-04. Status: accepted.

## Context
XPConnect is about to be shared with other simmers. They need one place to download it, a way to hear about new
versions, and a plugin that stays in step with the app. Ben has no code-signing certificate, so Windows treats
XPConnect.exe as coming from an unknown publisher. BeyondATC runs only on Windows.

## Decision
- **Releases live on GitHub.** The Release workflow (`.github/workflows/release.yml`), started by hand on main,
  reads `<Version>` from `Directory.Build.props`, builds and tests on Windows, checks the zip (app, plugin, no test
  data), tags `v<Version>` and makes a **draft** release with the zip and its SHA-256. Ben publishes the draft.
  A version with a dash (`0.6.0-beta.1`) becomes a pre-release. Steps: `docs/releasing.md`.
- **The app only checks; it never installs.** At start-up the window asks GitHub's `releases/latest` (published,
  non-pre-release only) once, in the background, with a 10 s timeout. When it is newer, the ABOUT page shows
  NEW VERSION with a key that opens the release page. Any failure (offline, private repository, rate limit) shows
  nothing. `xpconnect update` prints the same; `xpconnect settings update-check off` turns it off.
- **Updating is unzipping over the old folder.** Settings and logs live in `%LOCALAPPDATA%\XPConnect`, so nothing
  is lost. The plugin travels in the zip; SETUP already compares the installed plugin file by file and offers
  UPDATE> (X-Plane must be closed, it locks the plugin).
- **The plugin is built for Windows only.** BeyondATC is Windows-only, so a Linux or Mac plugin has no users. The
  CMake files still build on Linux for local testing; CI no longer does.

## Alternatives
- **Self-update (download, swap the exe, restart).** Possible (Windows lets a running exe be renamed), but it adds a
  downloader, hash check, file swap with the stand-in process and plugin, rollback, and folders the user cannot
  write to (Program Files). A mistake there can only be fixed by users updating by hand, and a download made by the
  app skips Windows' download checks. Revisit once releases are routine, ideally with signed builds.
- **Velopack / Squirrel / MSIX installers.** Proper auto-update, but they install per user in their own folder,
  need packaging work, and MSIX needs a trusted signature. Too heavy for an experimental tool.
- **Checking on every page view or on a timer.** One check per start is enough and stays far below GitHub's
  60 requests an hour for unauthenticated callers.
