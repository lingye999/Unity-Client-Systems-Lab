# Native wooden fence fragments

Extracted by `ForgottenMemories/tools/flat_forest/extract_fences.py` from the existing original `TileSet.png`. All runtime pixels are exact 32×32 RGBA crops. No new artwork, rotation, resampling, alpha removal or recoloring. The preview is enlarged with nearest-neighbor only for audit.

The native catalog contains horizontal/vertical endpoints, intermediate posts, corners, T-junctions and a cross. The planner uses axis-aligned open polylines and picks a connectivity mask per shared post. It does not repeat the old preassembled `fence_short` strip. Native root anchor is (16,27), spacing is 32 pixels. Colliders follow the matching root-to-root lines with 4px half thickness; runtime depth sorting uses each post's root Y. JSON records every source rectangle and the original source SHA-256.
