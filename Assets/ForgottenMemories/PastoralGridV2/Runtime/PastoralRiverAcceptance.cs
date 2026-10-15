using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace ForgottenMemories.PastoralGridV2
{
    public sealed class PastoralRiverAcceptance : MonoBehaviour
    {
        public string outputDirectory;

        [Serializable]
        public sealed class Case
        {
            public int seed, style, navNodes, navSweeps, groundTiles, riverTiles, wetPhysicsPoints, minWidth,
                maxWidth, trees, shrubs, fencePieces, variants, phases;
            public bool water, passed, replay;
            public float density;
            public string prefix, error;
            public double generationMs;
        }

        [Serializable]
        public sealed class Result
        {
            public bool passed, animationPassed, invalidSeedPreservesMap, resourceOverlayStable;
            public int distinctAnimationFrames;
            public string algorithmVersion = "pastoral-dualgrid-2.2",
                          scope = "Actual Unity Tilemap/native animated frames, Physics2D overlap and " +
                                  "swept player clearance, water collision, rooted sorting, deterministic " +
                                  "layouts. Editor generation timings are not Player performance.";
            public List<Case> cases = new List<Case>();
            public string[] failures;
        }

        readonly Result result = new Result();
        readonly List<string> failures = new List<string>();

        IEnumerator Start()
        {
            yield return null;
            var r = FindObjectOfType<PastoralGridV2Runtime>();
            if (r == null)
            {
                failures.Add("Missing runtime");
                Finish();
                yield break;
            }
            foreach (int style in new[] { 1, 3 })
                foreach (int seed in new[] { 101, 202, 303 })
                {
                    var c = new Case { style = style, seed = seed, density = 1, water = true };
                    result.cases.Add(c);
                    try
                    {
                        Generate(r, c);
                        c.prefix = "river_" + r.Layout.profile + "_" + seed;
                        Capture(r, c.prefix);
                        File.WriteAllText(Path.Combine(outputDirectory, c.prefix + "_layout.json"),
                                          JsonUtility.ToJson(r.GridLayout));
                        File.WriteAllText(Path.Combine(outputDirectory, c.prefix + "_fences.json"),
                                          JsonUtility.ToJson(r.FencePlan, true));
                        c.passed = true;
                    }
                    catch (Exception e)
                    {
                        Fail(c, e);
                    }
                    yield return null;
                    if (c.passed && style == 3 && seed == 202)
                    {
                        FocusBank(r, 768);
                        var signatures = new HashSet<string>();
                        var frames = new HashSet<int>();
                        var position = new Vector3Int(24, -r.GridLayout.river.centerY[768] / 32 - 1, 0);
                        for (int f = 0; f < 6; f++)
                        {
                            yield return new WaitForSeconds(.15f);
                            string path = Path.Combine(outputDirectory, "river_animation_" + f + ".png");
                            ForestCameraCapture.Capture(r.mapCamera, path, 960, 720);
                            signatures.Add(Convert.ToBase64String(
                                System.Security.Cryptography.SHA256.Create().ComputeHash(
                                    File.ReadAllBytes(path))));
                            frames.Add(r.RiverTilemap.GetAnimationFrame(position));
                        }
                        result.distinctAnimationFrames = signatures.Count;
                        result.animationPassed = signatures.Count >= 3 && frames.Count >= 3;
                        if (!result.animationPassed)
                            failures.Add("Rendered water animation did not advance through distinct frames");
                    }
                }
            foreach (float density in new[] { .6f, 1.4f })
            {
                var c = new Case { style = 3, seed = -1, density = density, water = true };
                result.cases.Add(c);
                try
                {
                    Generate(r, c);
                    c.passed = true;
                }
                catch (Exception e)
                {
                    Fail(c, e);
                }
                yield return null;
            }
            for (int style = 0; style < 4; style++)
            {
                var c = new Case { style = style, seed = 101, density = 1, water = false };
                result.cases.Add(c);
                try
                {
                    Generate(r, c);
                    c.passed = true;
                }
                catch (Exception e)
                {
                    Fail(c, e);
                }
                yield return null;
            }
            var old = r.Layout;
            result.invalidSeedPreservesMap =
                !r.TryGenerateText("invalid seed") && ReferenceEquals(old, r.Layout);
            if (!result.invalidSeedPreservesMap)
                failures.Add("Invalid seed replaced old scene");
            int count = r.NewAssets.Count;
            for (int i = 0; i < 10; i++)
                r.SetDiagnostics(i % 2);
            r.SetDiagnostics(0);
            result.resourceOverlayStable = count == r.NewAssets.Count;
            if (!result.resourceOverlayStable)
                failures.Add("Diagnostic overlay asset leak");
            r.withRiver = true;
            r.style = 3;
            r.seed = 202;
            r.vegetation = 1;
            r.showControls = true;
            r.closeView = true;
            r.Regenerate();
            yield return null;
            Finish();
        }

        void Generate(PastoralGridV2Runtime r, Case c)
        {
            r.seed = c.seed;
            r.style = c.style;
            r.vegetation = c.density;
            r.withRiver = c.water;
            r.showControls = false;
            r.closeView = false;
            r.diagnosticMode = 0;
            r.Regenerate();
            c.generationMs = r.GenerationMilliseconds;
            var repeat = PastoralGridGenerator.Generate(c.seed, c.style, r.Catalog, c.density, c.water);
            var fences = PastoralRiverFencePlanner.Generate(repeat, r.Catalog);
            PastoralGridGenerator.RebuildNavigation(repeat, r.Catalog, fences.barriers);
            repeat.map.validation = PastoralGridGenerator.Validate(repeat, r.Catalog);
            c.replay = JsonUtility.ToJson(repeat) == JsonUtility.ToJson(r.GridLayout) &&
                       JsonUtility.ToJson(fences) == JsonUtility.ToJson(r.FencePlan);
            if (!c.replay)
                throw new Exception("Deterministic replay mismatch");
            Verify(r, c);
        }

        static void Verify(PastoralGridV2Runtime r, Case c)
        {
            var m = r.Layout;
            var g = r.GridLayout;
            const int layerMask = 1 << 8;
            if (!m.validation.passed)
                throw new Exception("Core validation failed");
            if (m.highlands.Length != 0 || m.waterfall != null || m.terrainSprites.Length != 0)
                throw new Exception("Unexpected height geometry");
            c.trees = m.validation.trees;
            c.shrubs = g.shrubCount;
            c.fencePieces = r.FencePlan.pieces.Length;
            Tilemap ground = null;
            foreach (var t in r.Generated.GetComponentsInChildren<Tilemap>())
                if (t.name == "Grass and dirt - four shared logical corners")
                    ground = t;
            if (ground == null)
                throw new Exception("Missing editable ground");
            for (int y = 0; y < m.height; y++)
                for (int x = 0; x < m.width; x++)
                {
                    int i = y * m.width + x, s = y * g.sampleWidth + x;
                    int mask = g.soilSamples[s] | g.soilSamples[s + 1] << 1 |
                               g.soilSamples[s + g.sampleWidth + 1] << 2 |
                               g.soilSamples[s + g.sampleWidth] << 3;
                    if (mask != g.soilMasks[i] || mask == 5 || mask == 10)
                        throw new Exception("Shared ground corners changed");
                    var p = new Vector3Int(x, -y - 1, 0);
                    var tile = ground.GetTile<Tile>(p);
                    if (tile == null ||
                        tile.name != PastoralGridRaster.GroundTileId(mask, m.groundVariant[i]) ||
                        tile.sprite.rect.width != 32 || tile.sprite.pixelsPerUnit != 32)
                        throw new Exception("Native ground material/scale mismatch");
                    if (Vector2.Distance(ground.GetCellCenterWorld(p),
                                         PastoralGridV2Runtime.World(x * 32 + 16, y * 32 + 16)) > .001f)
                        throw new Exception("Ground half-cell alignment");
                    c.groundTiles++;
                }
            Physics2D.SyncTransforms();
            for (int i = 0; i < m.navWalkable.Length; i++)
                if (m.navWalkable[i] != 0)
                {
                    var p =
                        PastoralGridV2Runtime.World(i % m.displayWidth * 16 + 8, i / m.displayWidth * 16 + 8);
                    var hit = Physics2D.OverlapCircle(p, .3f, layerMask);
                    if (hit != null)
                        throw new Exception("Navigation overlaps " + hit.name + " at " + i);
                    c.navNodes++;
                    for (int d = 0; d < 2; d++)
                        if ((m.navLinks[i] & 1 << d) != 0)
                        {
                            var cast = Physics2D.CircleCast(p, .3f, d == 0 ? Vector2.right : Vector2.down,
                                                            .5f, layerMask);
                            if (cast.collider != null)
                                throw new Exception("Navigation sweep hits " + cast.collider.name + " at " +
                                                    i);
                            c.navSweeps++;
                        }
                }
            foreach (var p in r.PropRenderers)
                if (p.sortingOrder != ForestDepth.Order(p.transform.position.y) + 1)
                    throw new Exception("Vegetation root sorting");
            foreach (var p in r.FenceRenderers)
                if (p.sortingOrder != ForestDepth.Order(p.transform.position.y) + 1)
                    throw new Exception("Fence root sorting");
            foreach (var b in r.FencePlan.barriers)
            {
                var p = (PastoralGridV2Runtime.World(b.a) + PastoralGridV2Runtime.World(b.b)) * .5f;
                if (Physics2D.OverlapPoint(p, layerMask) == null)
                    throw new Exception("Fence has no physical barrier");
            }
            if (!c.water)
            {
                if (g.river != null || r.RiverTilemap != null || r.RiverColliders.Count != 0)
                    throw new Exception("Water option off left river geometry");
                return;
            }
            if (g.river == null || r.RiverTilemap == null)
                throw new Exception("Missing river");
            c.minWidth = g.river.minWidthPixels;
            c.maxWidth = g.river.maxWidthPixels;
            if (c.maxWidth - c.minWidth < 32)
                throw new Exception("River has no width variation");
            var variants = new HashSet<int>();
            var phases = new HashSet<int>();
            var checkedFrames = new HashSet<PastoralRiverTile>();
            for (int y = 0; y < m.height; y++)
                for (int x = 0; x < m.width; x++)
                {
                    int mask = g.river.waterMasks[y * m.width + x];
                    var position = new Vector3Int(x, -y - 1, 0);
                    var tile = r.RiverTilemap.GetTile<PastoralRiverTile>(position);
                    if (mask == 0)
                    {
                        if (tile != null)
                            throw new Exception("Dry ground contains a water tile");
                        continue;
                    }
                    int s = y * g.river.sampleWidth + x,
                        shared = g.river.waterSamples[s] | g.river.waterSamples[s + 1] << 1 |
                                 g.river.waterSamples[s + g.river.sampleWidth + 1] << 2 |
                                 g.river.waterSamples[s + g.river.sampleWidth] << 3;
                    if (mask != shared || mask == 5 || mask == 10 || tile == null || tile.waterMask != mask ||
                        tile.frames.Length != 6)
                        throw new Exception("Rendered shoreline corner/animation mismatch");
                    if (Vector2.Distance(r.RiverTilemap.GetCellCenterWorld(position),
                                         PastoralGridV2Runtime.World(x * 32 + 16, y * 32 + 16)) > .001f)
                        throw new Exception("River half-cell misalignment");
                    if (mask == 15)
                    {
                        var hit = Physics2D.OverlapPoint(
                            PastoralGridV2Runtime.World(x * 32 + 16, y * 32 + 16), layerMask);
                        if (hit == null || !hit.name.StartsWith("River solid ", StringComparison.Ordinal))
                            throw new Exception("Water missing solid collision");
                        c.wetPhysicsPoints++;
                    }
                    if (checkedFrames.Add(tile))
                        for (int f = 0; f < 6; f++)
                        {
                            var sprite = tile.frames[f];
                            if (sprite.rect.width != 32 || sprite.pixelsPerUnit != 32 ||
                                sprite.texture.filterMode != FilterMode.Point ||
                                sprite.texture.wrapMode != TextureWrapMode.Clamp)
                                throw new Exception("Scaled/filtered native water");
                            var raw = sprite.texture.GetPixels32();
                            var expected = r.RiverRaster.TilePixels(mask, f, x, y, c.seed);
                            for (int py = 0; py < 32; py++)
                                for (int px = 0; px < 32; px++)
                                {
                                    var a = raw[(31 - py) * 32 + px];
                                    var b = expected[py * 32 + px];
                                    if (a.r != b.r || a.g != b.g || a.b != b.b || a.a != b.a)
                                        throw new Exception("Native river frame pixels changed");
                                }
                        }
                    variants.Add(tile.variant);
                    phases.Add(tile.phase);
                    c.riverTiles++;
                }
            c.variants = variants.Count;
            c.phases = phases.Count;
            if (c.riverTiles != g.river.wetTileCount || r.RiverColliders.Count != c.riverTiles ||
                c.variants < 2 || c.phases < 3)
                throw new Exception("River coverage/collision/variation incomplete");
        }

        void Capture(PastoralGridV2Runtime r, string prefix)
        {
            r.closeView = false;
            r.FitCamera();
            ForestCameraCapture.Capture(r.mapCamera, Path.Combine(outputDirectory, prefix + "_overview.png"),
                                        1536, 1152);
            FocusBank(r, 768);
            ForestCameraCapture.Capture(r.mapCamera, Path.Combine(outputDirectory, prefix + "_close.png"),
                                        960, 720);
            int narrow = 160, broad = 160;
            for (int x = 160; x < 1376; x += 16)
            {
                int width = r.GridLayout.river.bankBottom[x] - r.GridLayout.river.bankTop[x];
                if (width < r.GridLayout.river.bankBottom[narrow] - r.GridLayout.river.bankTop[narrow])
                    narrow = x;
                if (width > r.GridLayout.river.bankBottom[broad] - r.GridLayout.river.bankTop[broad])
                    broad = x;
            }
            FocusBank(r, narrow);
            ForestCameraCapture.Capture(r.mapCamera, Path.Combine(outputDirectory, prefix + "_narrow.png"),
                                        960, 720);
            FocusBank(r, broad);
            ForestCameraCapture.Capture(r.mapCamera, Path.Combine(outputDirectory, prefix + "_broad.png"),
                                        960, 720);
        }

        static void FocusBank(PastoralGridV2Runtime r, int x)
        {
            r.mapCamera.rect = new Rect(0, 0, 1, 1);
            r.mapCamera.orthographicSize = 4.5f;
            r.mapCamera.transform.position =
                new Vector3(Mathf.Clamp(x / 32f, 6, 42),
                            -Mathf.Clamp((r.GridLayout.river.bankBottom[x] + 12) / 32f, 4.5f, 31.5f), -10);
        }

        void Fail(Case c, Exception error)
        {
            c.error = error.ToString();
            failures.Add(c.style + "/" + c.seed + "/" + c.water + ": " + error.Message);
            Debug.LogWarning("PASTORAL_RIVER_CASE " + error);
        }

        void Finish()
        {
            result.failures = failures.ToArray();
            result.passed = failures.Count == 0 && result.cases.Count == 12;
            File.WriteAllText(Path.Combine(outputDirectory, "river_unity_acceptance.json"),
                              JsonUtility.ToJson(result, true));
            Debug.Log("PASTORAL_RIVER_ACCEPTANCE " + result.passed + " " + outputDirectory);
        }
    }
}
