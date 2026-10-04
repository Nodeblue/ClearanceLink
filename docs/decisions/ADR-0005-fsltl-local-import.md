# ADR-0005: Use FSLTL models by converting the user's own install locally

Date: 2026-10-03. Status: accepted (Ben asked for it; first version built, X-Plane check pending).

## Context
BeyondATC names its traffic by FSLTL title (e.g. `FSLTL_FAIB_A21N_JST-Jetstar Airways`). The plugin (ADR-0004) draws them
with whatever CSL package is installed, usually Bluebell, matched only by ICAO type and airline. FSLTL has about 3,200
airline liveries on about 100 airliner models, many more than Bluebell, and Ben has it installed for MSFS.

FSLTL's models are plain glTF + DDS (not encrypted): `docs/research/fsltl-models.md`. FSLTL's terms
(https://fslivetrafficliveries.com/) forbid redistributing any part of it and say the models belong to their original
authors. They say nothing about a user converting their own copy.

## Decision
`clearancelink fsltl import` reads the user's FSLTL package (found from MSFS's `UserCfg.opt`, or `--fsltl`) and writes an
X-Plane CSL package to `<X-Plane>/Custom Data/CSL/ClearanceLink-FSLTL` on the same PC: one .obj per model and texture
(XPMP2 swaps the livery texture per entry), DXT DDS textures capped at `--size` (default 2048), an `xsb_aircraft.txt`
with one `LIVERY <type> <airline> <key>` entry per livery, and `fsltl-catalogue.json` (title to key). `serve` loads that
catalogue and sends BATC's exact livery key to the plugin, so XPMP2 picks the right livery; other titles keep the
type/airline rules and fall back to Bluebell.

Nothing from FSLTL is ever in this repo, our releases or troubleshooting bundles. Microsoft's own models (the
`FSLTL_Asobo_*` GA aircraft) are not converted.

## Alternatives
- Ship converted FSLTL models: not allowed by FSLTL's terms.
- Convert on demand, when BATC first asks for a livery: XPMP2 only reads CSL packages when it loads them, so a new
  livery would not show until plugins reload.
- Read glTF at runtime in the plugin: far more work (a glTF renderer in an X-Plane plugin) and slower to load than OBJ8.
- Stay with Bluebell only: works, but fewer and less accurate liveries.

## Consequences
- Users without MSFS/FSLTL get nothing new; Bluebell stays the default.
- Ask the FSLTL team before publicising the importer in a public release.
- Conversion quality depends on how each model was exported. Known gaps: gear parts that rotate about more than one
  axis are approximated; materials without a texture are left out; `FSLTL_SU95` (730k triangles) is skipped.
