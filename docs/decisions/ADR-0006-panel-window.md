# ADR-0006: An FMS-style window built with Avalonia

Date: 2026-10-02. Status: accepted.

## Context
Until now, double-clicking ClearanceLink.exe opened a console that scrolled log lines, and batch files switched
the ATC app's set-up. Ben wants something a non-technical simmer finds friendly and appealing: an FMS/CDU-style
screen (24 columns, line select keys, cyan labels, white data) that works with mouse and keys. The web status page
stays as the detailed view. He picked an app window over a console TUI (2026-10-02): a console's look depends on
each user's font and settings, it breaks on resize, and Quick Edit mode can freeze it.

## Decision
- **Avalonia 11.3** draws the window, in the same ClearanceLink.exe (`src/ClearanceLink.Host/Panel`). Double-clicking opens
  the window, which runs `serve` in the background until it is closed. Commands still work from a command prompt:
  the exe is a Windows GUI program that attaches to the prompt's console when it has arguments
  (`Panel/ConsoleAttach.cs`). `clearancelink start` keeps the old console flow.
- The pages are built by `CduPages.Build` from a `PanelState` record, with no UI types, so the content is tested
  directly. The window only draws them and maps keys to actions.
- The screen font is B612 Mono, made for Airbus cockpit displays, under the SIL Open Font License (embedded,
  `Panel/Fonts/OFL.txt`).
- First run opens on WELCOME pages (`CduPages.StartPages`) in the same screen, instead of a separate wizard. They are
  messages (`CduPage.Message`, plain sentences in one size) above the rows that have keys, so advice never looks
  selectable. A new user learns the line select keys while setting up. Done is a flag in settings.json.
- Tests run the real window headless (Avalonia.Headless + Skia) and can save each page as a PNG
  (`CLEARANCELINK_SCREENSHOTS=<dir> dotnet test`), so agents can see what they changed.

## Alternatives
- **WPF or WinForms.** Windows-only, so neither builds nor tests in the Linux cloud sessions and CI where most of
  this project is written.
- **Console TUI (Terminal.Gui).** Mouse works, but the look depends on the user's console. Rejected by Ben.
- **Avalonia 12.** Newer, but 11.3 is the long-standing stable line with known APIs; moving later is a small change.
- **A second exe for the window.** Keeps the CLI a console app, but doubles the self-contained runtime in the zip.

## Consequences
- The zip grows by Avalonia and Skia (single file, native libraries extracted on first run).
- A command run from PowerShell or cmd prints after the prompt has returned, as with any GUI-subsystem program. That's
  fine for occasional use; scripts can redirect output to a file.
