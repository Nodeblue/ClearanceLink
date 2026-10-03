# Using FSLTL models for XPConnect's X-Plane traffic

Investigation, 2026-10-03. Read-only on `...\Community\fsltl-traffic-base` (FSLTL Traffic Base 1.5.0, MSFS 2024 Community folder). Nothing was copied out of it; one mesh was decoded in memory to confirm the vertex format.

## Recommendation

Yes, it is technically feasible, and it is the best way to get airline-accurate traffic. Build it as an **opt-in local importer**: an `xpconnect fsltl import` step (and a Setup-page button) that reads the user's own FSLTL install and writes an X-Plane CSL package into their own X-Plane folder. Never ship, host or upload converted FSLTL content. Keep Bluebell as the fallback for anything not imported, and for users without MSFS/FSLTL.

Before publishing a release that includes the importer, ask the FSLTL team for their blessing. Their terms forbid redistribution but say nothing about a user converting their own copy, and the models belong to their original authors (FAIB, FSPX, TFS and others).

## Critical review (second pass, same day)

Re-checked against XPMP2 v3.7.0's source (`src/CSLModels.cpp`) and our plugin/host code. Corrections to the first pass:

1. **The 100% name match is partly circular.** `mapping/liveries.json` (the model list XPConnect offers BATC) was captured from this same MSFS install, so of course it matches. For Ben it holds; for another user with a different FSLTL version it may not. Fix: the importer writes its own catalogue, and XPConnect should offer BATC the titles the user actually imported (plus Bluebell-backed ones), not Ben's list.
2. **On-demand import does not fit XPMP2.** XPMP2 scans CSL packages when they are loaded, so converting a livery the first time BATC asks for it would not show until X-Plane reloads plugins. Import up front (all, or a filter such as types/airlines) instead.
3. **One texture per OBJ.** An X-Plane `.obj` has exactly one texture, but FSLTL airliners have two materials (livery atlas + a separate window/porthole material). XPMP2 allows several `OBJ8` lines per CSL entry, so each material becomes its own `.obj` drawn together.
4. **Livery keys must be one word.** XPMP2 tokenises `xsb_aircraft.txt` on whitespace, so the exact BATC title cannot be the `LIVERY` key. Use the FSLTL folder name (plus the `[FLTSIM.n]` index when a folder has several) and look it up from the title in XPConnect. Today XPConnect sends no livery to the plugin at all (`TrafficBridge.cs:96` passes `null`).
5. **Per-livery textures are cheap in XPMP2.** `OBJ8 SOLID YES model.obj livery.dds` makes XPMP2 write a small `.xpmp2.obj` copy with the texture swapped, so one converted model serves all its liveries; only textures are per livery.
6. **Texture conversion is cheaper than first stated.** FSLTL DDS files already contain mipmaps, so a 4096 DXT1/DXT5 texture becomes 2048 or 1024 by dropping the top mip levels: no decode, no quality loss, near-instant. Only BC7 (about 36% of livery textures) needs decoding and re-encoding, unless X-Plane 12 accepts BC7 (not documented; to be tested).
7. **LOD makes the frame cost lower than estimated.** FSLTL ships LOD0 and LOD3 meshes for most airliners; both can go in one `.obj` with `ATTR_LOD`, so distant traffic draws the ~1-2k triangle version.
8. **Effort was optimistic.** Gear/wheel/spoiler animations need converting from glTF node keyframes to OBJ8 `ANIM_` blocks, and the transform/axis conventions differ by exporter. Plan: static parts first, gear second, then the rest.

## What is in the package

| | |
|---|---|
| Size | 16,333 files, 45.4 GB, almost all of it (3,759 files) DDS textures; models are only about 210 MB |
| Aircraft folders | 2,326 (base models + one folder per livery set), 3,211 liveries (`[FLTSIM.n]` entries) |
| Types | 54 ICAO types; most liveries on B738 (304 folders), A320 (273), A20N (137), A321, A21N, A319, A333, B789 |
| Airlines | 1,110 ICAO airline codes (`icao_airline=` in each livery's `aircraft.cfg`) |
| Base models | 233 `.gltf` files (98 airliner LOD0 models + LOD1/3/4/6 variants, plus 10 Asobo GA aircraft) |
| Livery layout | Each livery folder has only `aircraft.cfg`, `model.CFG` (points at the base model's XML) and one albedo texture; everything else falls back to the base folder |

### Model format
- Plain glTF 2.0 JSON + `.bin`, **not encrypted or compiled**. Exported with Asobo's Blender/3ds Max MSFS exporters.
- Required extension: `MSFT_texture_dds` only. Others used: `ASOBO_asset_optimized` (all 233), `ASOBO_normal_map_convention`, `ASOBO_macro_light` (53), a few material extensions (glass, windshield, draw order).
- `ASOBO_asset_optimized` packs vertices non-standardly: UVs are **float16 stored in a SHORT accessor**, normals/tangents are signed bytes. Confirmed on the A20N: decoding UVs as half floats gives 0.22 to 0.94 (sane), normals decode to unit length. A converter must handle this; generic glTF tools get it wrong.
- Animations are glTF keyframe channels named after MSFS variables: `custom_anim_GEAR_ANIMATION_POSITION_*`, `*_WHEEL_ROTATION_ANGLE_*`, `SPOILERS_*`, `SIM_ON_GROUND_*`, `N1_blur_*`. These map cleanly onto XPMP2's gear, wheel, spoiler and prop/N1 datarefs.
- Typical airliner: 2 materials (livery + windows), ~74 mesh pieces, ~21 animations.

### Textures
- Liveries: 4096x4096 for most (1,214 DXT5 + 619 BC7), 2048 for many more; about 22 MB each at 4K with mips.
- Format mix: DXT5 1,874, BC7 1,073, DXT1 440, BC5 (normal maps) 257. X-Plane is safest with DXT1/DXT5, so BC7 needs re-encoding.
- Materials are MSFS PBR: albedo (`_T`), occlusion/roughness/metallic (`_M`), separate emissive for windows. X-Plane OBJ wants albedo + optional normal (roughness/metal in its channels) + `_LIT`. A first version can use albedo only plus a lit texture from the emissive window texture.

## Name matching with BATC

Excellent. Every one of the 3,211 FSLTL titles in this package appears verbatim in BATC's model list (`mapping/liveries.json`, 3,202 FSLTL entries), and each title maps to exactly one folder and texture. So the importer can key the CSL entries on BATC's exact title, and the plugin can do an exact match instead of today's closest-Bluebell guess. The 831 `-STUB` liveries are FSLTL placeholders (generic white or base texture with a code), and should rank below a real livery.

## Frame-rate and memory cost vs Bluebell

Measured on Ben's PC:

| | Bluebell (`Custom Data\CSL`, 2,311 .obj, 4.8 GB) | FSLTL airliners (LOD0) |
|---|---|---|
| Triangles per aircraft | median 2,967, p90 4,045 | median 5,380, p90 9,769 |
| Livery texture | 1024 DXT5 (~1.4 MB) | 4096 (~22 MB) or 2048 (~5.6 MB) |

- **GPU geometry:** roughly 2x Bluebell's triangles. 100 aircraft is about 0.5 M triangles, which is small for X-Plane 12 on a modern GPU. Draw calls matter more than triangles; a converter should merge each model into a few batches (static + one per animated part), similar to Bluebell.
- **VRAM is the real cost.** At native 4K, 50 distinct liveries is about 1.1 GB of VRAM; Bluebell is about 70 MB. Downscaling to 2048 at import (about 280 MB for 50) is the sensible default; 1024 matches Bluebell exactly. Make it a setting.
- **Outliers to exclude or reduce:** `FSLTL_SU95` LOD0 is 730k triangles (use its lower LOD or decimate). The 10 Asobo GA aircraft (C172, DA40, TBM etc.) are Microsoft's own models, 50k to 160k triangles, and are not FSLTL's to give; leave those on Bluebell.
- **Loading:** XPMP2 loads objects asynchronously, so first appearance of a new livery should not stutter, but importing up front avoids any runtime conversion cost.

## What building it would take

A C# importer in `XPConnect.Host` (fits the existing stack, unit-testable on Linux with synthetic glTF fixtures, no FSLTL files in the repo):
1. **Find FSLTL:** read MSFS's `UserCfg.opt` `InstalledPackagesPath` (2020 and 2024), look for `fsltl-traffic-base`. Let the user browse if not found.
2. **Parse liveries:** each `aircraft.cfg` `[FLTSIM.n]` gives title, ICAO type, airline, model folder and texture folder, following `base_container` and `texture.CFG` fallbacks.
3. **Convert each base model once** (about 100 airliner models, not 3,211): glTF + Asobo vertex packing to OBJ8, flatten node transforms, axis swap, map animations to XPMP2 datarefs, pick a lower LOD when LOD0 is too heavy.
4. **Convert each livery texture:** decode BC7/DXT, resize to the chosen size, write DXT5 `.dds`. BCnEncoder.NET (MIT) or Microsoft's texconv (MIT) can do this. About 3,200 textures; at 2048 the output is about 18 GB, at 1024 about 4.5 GB. Importing only the liveries BATC actually requests, on demand, is an option that keeps disk use small.
5. **Write `xsb_aircraft.txt`:** one `OBJ8_AIRCRAFT` per livery, the shared `.obj` with that livery's texture, `ICAO`/`AIRLINE` plus the exact BATC title as the livery key.
6. **Plugin side (small):** pass the BATC title through as the livery so XPMP2 matches exactly; the 0.4.0 wheel fit already works from `.obj` files, so ground placement comes for free.

Rough size: a working importer for the FAIB-family airliners (most of the liveries) is a few focused days of agent work; the long tail of model families with different exporters (FSPX, TFS, babylon.js-exported models) and material polish takes longer. The main technical risks are axis/transform conventions per exporter and getting gear/wheel animations right, both checkable automatically with the existing wheel-fit tool.

## Licence

- FSLTL's terms ([fslivetrafficliveries.com](https://fslivetrafficliveries.com/)): no element of the project (injector, models, liveries) may be uploaded or redistributed elsewhere without the FSLTL team's permission; freeware, no commercial use; the models are used with permission and remain their original creators' content, and FSLTL can give no permission for those models.
- The terms do not address a user converting their own installed copy for personal use. The usual community norm is that private conversion for your own use is fine, but reposting a conversion needs the creator's permission ([MSFS forum thread on FSLTL agreements](https://forums.flightsimulator.com/t/fslivetraffic-liveries-fsltl-aig-fsltl-flai-agreements/348174)).
- So: **shipping converted models: not allowed.** **Shipping a tool that converts the user's own install locally, with no FSLTL content in our repo or downloads:** likely acceptable, but a grey area. Asking FSLTL first is cheap and protects the project's reputation.
- Keep the importer free (no paid tier around it), credit FSLTL and the model authors in the UI, and never upload converted output (including in troubleshooting log bundles).
