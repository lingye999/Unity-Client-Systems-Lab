# Native material contract

`PastoralGridRaster` uses the frozen `ForestMap/forest_atlas` and audited fragments from `ForestMap/understory` and `ForestMap/paving_dense`. River animation also reads `ForestMap/waves`; fences read `ForestMap/fences`. Canonical original PNGs remain in the maintainer's local art folder and are not included in this repository. Attribution and usage status are recorded in the repository's THIRD_PARTY_NOTICES.md. Unreferenced imported copies and obsolete preview fragments have been removed.

The logical samples are at `(32*x,32*y)`. Each display cell occupies the square between four shared samples. The display centre is offset by `(16,16)` from its northwest logical sample. Unity places that tile at `(x,-y-1)`; its centre is `(x+.5,-y-.5)` world units at 32 pixels per unit. There is no additional half-cell translation.

Bits run clockwise: northwest=1, northeast=2, southeast=4, southwest=8. The existing native catalog uses **grass bits**, while V2 stores **soil bits**. Rendering uses `nativeGrassMask = 15 XOR soilMask`. Masks 05 and 10 are not in the native kit. The generator repairs their shared logical samples; the renderer explicitly throws if either reaches it.

All coordinates below refer to the original `ForgottenMemories/TileSet.png`, top-left origin, 32×32 pixels. Packed atlas positions have a different meaning.

| Native grass mask | Original x | Original y |
|---:|---:|---:|
| 00 | 0 | 0 |
| 01 | 128 | 96 |
| 02 | 32 | 128 |
| 03 | 64 | 128 |
| 04 | 32 | 0 |
| 06 | 0 | 64 |
| 07 | 256 | 32 |
| 08 | 96 | 0 |
| 09 | 128 | 64 |
| 11 | 192 | 32 |
| 12 | 64 | 0 |
| 13 | 192 | 96 |
| 14 | 256 | 96 |
| 15 | 64 | 64 |

The transition tiles are fully opaque, including their brown soil background. They require no alpha underlay. Fully grassy cells use the existing flat native grass colour; fully soil cells use `grass_00`, the plain native brown at `(0,0)`. It equals the flat `dirt_0` crop at `(320,0)`. Grass details are placed only on fully grassy cells. `dirt_1` is not repeated as a full tile: it includes a large pebble patch that forms an obvious circular cadence when tiled.

White paving is a separate decorative layer. The existing exact crop `(1312,128,144,160)` contains native stone components. Runtime selects connected complete silhouettes, rejects any touching the crop boundary, preserves all their visible RGBA pixels, and rejects placements whose visible pixels extend beyond native soil or the road footprint. It never cuts a stone to fit a road. Junction candidates can receive small groups; main roads alternate bare and sparse sections; branches remain scant.

Small coloured gravel is extracted from the three whole non-background components in native `dirt_1`, original crop `(352,0,32,32)`. The original flat brown is excluded exactly; the three native components contain 5,17,12 visible pixels. The large 422-pixel component touches the bottom/right crop boundary and is rejected. Gravel is placed in uneven small groups using the same soil and route containment checks as paving. All visible gravel pixels retain their original RGBA values. The ground does not acquire a repeating circular texture.

Understory reuses the existing native dark-grass crop and reflected native half to close its open source edge. Stamps belong to tree groups, avoid activity clearings, and are clipped to visible native grass pixels. The navigation clearance buffer never clips this artwork: it otherwise creates straight cut lines separated from the actual road. Native tree contact shadows remain separate from tree bodies and retain root-based depth sorting.

The source-workspace audit `ForgottenMemories/tools/pastoral_grid_v2/audit_native_materials.py` (not included in this repository) verifies all fourteen native masks, the clockwise corner classification, exact source-to-atlas RGBA identity, opacity, and complete selectable stone components. Outputs are under `ForgottenMemories/ForestSample/PastoralGridV2/Materials/`.

## Scenic grass paths

The fourth style, `scenic_woodland`, uses the same shared grass/soil samples but does not require soil for its walk corridors. `routeSurface[index] == 1` identifies a grass route; the first three styles retain their soil placement rules. Navigation corridors still own route clearance and tree-root exclusion.

Scenic guidance uses complete native paving components selected for readable stepping size (width 9–18px, height 6–16px, at least 50 visible pixels). They are never rotated, enlarged or clipped. Two or three pieces form a small group; groups follow route arc length at unequal 43–62px intervals, with occasional bare intervals and sparse small native gravel. Every visible pixel must lie on native grass, inside its specific grass route with 2px edge clearance, and in a traversable navigation cell. Soil-route paving continues to require visible soil. `IsPavingPixel` exposes the combined material-and-corridor rule for verification.

The first scenic clearing can receive a small denser approach, using the same containment rules. These are flat decorative pixels without colliders; they do not close a fence opening or block walking. Paving follows the grass corridor without painting a mandatory continuous brown road underneath it.

## Optional native river in V2.2

`PastoralRiverRaster` adds an optional river overlay without changing the grass/soil material contract above. River samples are a separate shared 49×37 grid with 32px spacing, rendering 48×36 cells. Here a bit means **water**, so native source selection uses `nativeLandMask = 15 XOR waterMask`. Clockwise bits remain NW=1, NE=2, SE=4, SW=8. A dry cell (`waterMask == 0`) has no river overlay. A full-water cell selects native `water_00`. Unsupported water masks 05/10 must be repaired in shared samples before rendering.

The following 32×32 rectangles use the original `ForgottenMemories/WaterTiles-6frames.png`, top-left coordinates. Each next frame moves the source rectangle 32px to the right; there are six frames. The existing packed atlas already removes only the original flat land placeholder colour, revealing actual grass or forest-floor pixels below. It retains native rocks, submerged rim and foam; no mandatory dirt ring is painted around water.

| Native land mask | Original first-frame x | Original y |
|---:|---:|---:|
| 00 | 352 | 704 |
| 01 | 256 | 128 |
| 02 | 480 | 128 |
| 03 | 704 | 64 |
| 04 | 480 | 0 |
| 06 | 480 | 64 |
| 07 | 480 | 288 |
| 08 | 256 | 0 |
| 09 | 256 | 64 |
| 11 | 256 | 288 |
| 12 | 704 | 256 |
| 13 | 256 | 256 |
| 14 | 480 | 256 |

The existing `ForestMap/waves` texture is an exact 192×96 source derivative. Its three rows come from original crops `(32,288,192,32)`, `(32,320,192,32)` and `(32,352,192,32)`, each containing all six 32px frames. Three patterns and six local phases give 18 candidate sequences. A deterministic seed plan scores horizontal/vertical boundary differences over all six frames and discourages adjacent repetition. This reduces obvious repeated patterns and seams; it does not claim every neighbouring edge has identical pixels.

Only pixels that remain clear open water through all six native shoreline frames are replaced by pixels from a selected native wave sequence. The stable classification is opaque blue water with `r <= 110`, `g >= 125`, `b >= 140`, `b > r + 20`. Every pixel outside that set remains byte-identical to its original atlas frame, including native bank stones, wet rock, dark edges, foam and transparent land. No interpolation, uniform recolour or texture enlargement is used. `TilePixels` returns top-down 32×32 RGBA data; `IsOpenWaterPixel`, `IsWetPixel`, `WaterVariant`, `WaterPhase` and `OriginalSourceRect` expose the relevant audit decisions.

Display placement uses the same `(x,-y-1)` Tilemap cell as the underlying ground, with no extra half-cell translation. Geometry comes from the same water samples: midpoint water polygons drive physical water colliders, while navigation and root placement also require 8px bank protection because original rock silhouettes differ from the mathematical midpoint line. Pixel masks are a rendering check, not a replacement for that clearance geometry.

The maintainer's local source evidence is under `ForgottenMemories/ForestSample/PastoralGridV2/RiverMaterials/` (not included in this repository). `river_material_verification.json` records 78 original shoreline/frame crops and 18 wave crops, with zero source-pixel mismatches after the expected land-placeholder transparency conversion. Its contact sheet and channel animation are material previews, not Unity camera captures. Unity rendering, animation and Physics2D require the separate acceptance report.
