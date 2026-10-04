# Making a release

For the maintainer. Users read [install.md](install.md).

## Steps
1. In a PR, set `<Version>` in `Directory.Build.props` (for example `0.6.0`, or `0.6.0-beta.1` for a test build)
   and update `docs/STATUS.md`. Merge it.
2. On GitHub: **Actions > Release > Run workflow** (branch `main`). It runs every test, builds the zip with the
   X-Plane plugin, checks no test data got in, tags `v<Version>` and creates a **draft** release.
3. Open **Releases**, check the draft (notes, the zip and its `.sha256.txt`), edit the notes if you
   like, and press **Publish release**.

Nobody sees a draft. Once published, everyone's ClearanceLink shows NEW VERSION on its ABOUT page the next time it
starts. A version with a dash is published as a **pre-release**: testers can download it, but the update check
never offers it.

If the workflow stops with "already released", raise `<Version>` first. Pushing a tag `v<Version>` yourself also
works; the workflow refuses a tag that doesn't match `Directory.Build.props`.

The X-Plane plugin has its own version (`plugin/CMakeLists.txt`). Raise it when the plugin changes; users see
"NEWER VERSION" on SETUP whenever the plugin in the zip differs from the one installed.

## Before the first public release
- The repository must be public, or the update check (and downloads) only work for people with access.
- Decide on the two MSFS recordings in `recordings/samples` (see the project notes); the zip never contains them.

## Code signing (optional, later)
Without a signature, Windows SmartScreen warns on first start (users click **More info > Run anyway**, or tick
**Unblock** on the zip first; see install.md), and Windows 11's Smart App Control may block the app outright.
Some antivirus tools also flag unsigned single-file .NET apps. Ways to sign without buying a certificate yourself:
- **SignPath Foundation**: free signing for open-source projects with an OSI licence (MIT qualifies) and a public
  repository, from GitHub Actions. Needs an application and their review.
- **Azure Artifact Signing** (formerly Trusted Signing): a low monthly fee, identity checked by Microsoft;
  check whether individuals in your country are accepted.
Even signed, SmartScreen may warn until the certificate builds reputation. Signing would slot into the Release
workflow after `publish.ps1`, before the zip is made.
