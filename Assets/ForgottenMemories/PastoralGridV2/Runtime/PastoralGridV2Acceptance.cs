using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;
namespace ForgottenMemories.PastoralGridV2
{
    public sealed class PastoralGridV2Acceptance : MonoBehaviour
    {
        public string outputDirectory;

        [Serializable]
        public sealed class Case
        {
            public int seed, style, navNodes, navSweeps, trees, fencePieces, fenceGroups, regions, roads,
                ambiguousRepairs, wildTiles, groundTiles, greenTrees, warmTrees, cyanTrees, shrubs,
                shrubColonies, pathDecorationPixels;
            public float density;
            public double generationMs;
            public bool passed, replay, grassWalk;
            public string error;
        }

        [Serializable]
        public sealed class Result
        {
            public bool passed, invalidSeedPreservesMap, overlayResourcesStable;
            public string algorithmVersion,
                scope = "Unity Play Mode: reproducible seed, actual Physics2D clearance/sweeps, native " +
                        "fence collisions, root sorting, every rendered dual-grid tile and shared corner, " +
                        "independent camera captures. Editor timings are not player benchmarks.";
            public List<Case> cases = new List<Case>();
            public string[] failures;
        }

        readonly Result report = new Result();
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
            r.withRiver = false;
            for (int style = 0; style < 4; style++)
                foreach (int seed in new[] { 101, 202 })
                {
                    var c = new Case { seed = seed, style = style, density = 1 };
                    report.cases.Add(c);
                    try
                    {
                        r.seed = seed;
                        r.style = style;
                        r.vegetation = 1;
                        r.showControls = false;
                        r.closeView = false;
                        r.diagnosticMode = 0;
                        r.Regenerate();
                        c.generationMs = r.GenerationMilliseconds;
                        report.algorithmVersion = r.Layout.algorithmVersion;
                        var replay = PastoralGridGenerator.Generate(seed, style, r.Catalog, 1);
                        var fences = ScenicFencePlanner.Generate(replay, r.Catalog);
                        PastoralGridGenerator.RebuildNavigation(replay, r.Catalog, fences.barriers);
                        replay.map.validation = PastoralGridGenerator.Validate(replay, r.Catalog);
                        c.replay = JsonUtility.ToJson(replay) == JsonUtility.ToJson(r.GridLayout) &&
                                   JsonUtility.ToJson(fences) == JsonUtility.ToJson(r.FencePlan);
                        if (!c.replay)
                            throw new Exception("Replay mismatch");
                        Verify(r, c);
                        string prefix = r.Layout.profile + "_" + seed;
                        r.FitCamera();
                        ForestCameraCapture.Capture(
                            r.mapCamera, Path.Combine(outputDirectory, prefix + "_overview.png"), 1536, 1152);
                        r.closeView = true;
                        r.FitCamera();
                        // Capture uses 16:9 even when batch mode's hidden screen is
                        // 4:3. Clamp against the actual capture footprint.
                        var close = r.mapCamera.transform.position;
                        float halfHeight = r.mapCamera.orthographicSize, halfWidth = halfHeight * 1280 / 720;
                        r.mapCamera.transform.position =
                            new Vector3(Mathf.Clamp(close.x, halfWidth, 48 - halfWidth),
                                        Mathf.Clamp(close.y, -36 + halfHeight, -halfHeight), close.z);
                        ForestCameraCapture.Capture(
                            r.mapCamera, Path.Combine(outputDirectory, prefix + "_close.png"), 1280, 720);
                        if (r.FencePlan.pieces.Length > 0)
                        {
                            var f = r.FencePlan.pieces[0];
                            r.mapCamera.transform.position =
                                new Vector3(Mathf.Clamp(f.rootX / 32f, 6, 42),
                                            -Mathf.Clamp(f.rootY / 32f, 4.5f, 31.5f), -10);
                            r.mapCamera.orthographicSize = 4.5f;
                            ForestCameraCapture.Capture(
                                r.mapCamera, Path.Combine(outputDirectory, prefix + "_fences.png"), 960, 720);
                        }
                        var focus = r.Layout.entrance;
                        double best = double.MaxValue;
                        foreach (var node in r.GridLayout.roadNodes)
                            if (node.kind == "junction")
                            {
                                double d = (node.x - 768) * (node.x - 768) + (node.y - 576) * (node.y - 576);
                                if (d < best)
                                {
                                    best = d;
                                    focus = new Point(node.x, node.y);
                                }
                            }
                        r.mapCamera.orthographicSize = 4.5f;
                        r.mapCamera.transform.position = new Vector3(
                            Mathf.Clamp(focus.x / 32f, 6, 42), -Mathf.Clamp(focus.y / 32f, 4.5f, 31.5f), -10);
                        ForestCameraCapture.Capture(
                            r.mapCamera, Path.Combine(outputDirectory, prefix + "_junction.png"), 960, 720);
                        File.WriteAllText(Path.Combine(outputDirectory, prefix + "_layout.json"),
                                          JsonUtility.ToJson(r.GridLayout));
                        File.WriteAllText(Path.Combine(outputDirectory, prefix + "_fences.json"),
                                          JsonUtility.ToJson(r.FencePlan, true));
                        c.passed = true;
                    }
                    catch (Exception e)
                    {
                        c.error = e.ToString();
                        failures.Add(style + "/" + seed + ": " + e.Message);
                        Debug.LogWarning("PASTORAL_GRID_V2_CASE " + e);
                    }
                    yield return null;
                }
            foreach (int style in new[] { 1, 3 })
                foreach (float density in new[] { .6f, 1.4f })
                {
                    var c = new Case { seed = -1, style = style, density = density };
                    report.cases.Add(c);
                    try
                    {
                        r.seed = -1;
                        r.style = style;
                        r.vegetation = density;
                        r.Regenerate();
                        Verify(r, c);
                        c.generationMs = r.GenerationMilliseconds;
                        c.passed = true;
                    }
                    catch (Exception e)
                    {
                        c.error = e.ToString();
                        failures.Add("Density " + style + "/" + density + ": " + e.Message);
                    }
                    yield return null;
                }
            var old = r.Layout;
            report.invalidSeedPreservesMap =
                !r.TryGenerateText("not a seed") && ReferenceEquals(old, r.Layout);
            if (!report.invalidSeedPreservesMap)
                failures.Add("Invalid input replaced map");
            int owned = r.NewAssets.Count;
            for (int i = 0; i < 12; i++)
                r.SetDiagnostics(i % 2);
            r.SetDiagnostics(0);
            report.overlayResourcesStable = r.NewAssets.Count == owned;
            if (!report.overlayResourcesStable)
                failures.Add("Overlay resource leak");
            r.seed = 202;
            r.style = 3;
            r.vegetation = 1;
            r.closeView = true;
            r.showControls = true;
            r.Regenerate();
            yield return null;
            Finish();
        }

        static void Verify(PastoralGridV2Runtime r, Case c)
        {
            var m = r.Layout;
            var g = r.GridLayout;
            c.regions = g.regionInfo.Length;
            c.roads = g.roadInfo.Length;
            c.ambiguousRepairs = g.ambiguityCorrections;
            c.wildTiles = g.wildInteriorTiles;
            c.greenTrees = g.greenTrees;
            c.warmTrees = g.warmTrees;
            c.cyanTrees = g.cyanTrees;
            c.shrubs = g.shrubCount;
            c.shrubColonies = g.shrubColonies;
            c.pathDecorationPixels = r.PathDecorationPixels;
            c.grassWalk = g.scenicPaths;
            if (c.grassWalk)
            {
                foreach (int sample in g.soilSamples)
                    if (sample != 0)
                        throw new Exception("Scenic grass walk contains unwanted soil");
                if (c.pathDecorationPixels < 100)
                    throw new Exception("Scenic grass walk has no native paving guidance");
            }
            if (g.sampleWidth != m.width + 1 || g.sampleHeight != m.height + 1 || g.sampleStep != 32)
                throw new Exception("Dual grid sample dimensions");
            Tilemap ground = null;
            foreach (var t in r.Generated.GetComponentsInChildren<Tilemap>())
                if (t.name == "Grass and dirt - four shared logical corners")
                    ground = t;
            if (ground == null)
                throw new Exception("Missing native editable ground Tilemap");
            for (int y = 0; y < m.height; y++)
                for (int x = 0; x < m.width; x++)
                {
                    int i = y * m.width + x, s = y * g.sampleWidth + x;
                    int soilMask = g.soilSamples[s] | (g.soilSamples[s + 1] << 1) |
                                   (g.soilSamples[s + g.sampleWidth + 1] << 2) |
                                   (g.soilSamples[s + g.sampleWidth] << 3);
                    if (soilMask != g.soilMasks[i] || soilMask == 5 || soilMask == 10)
                        throw new Exception("Shared corner topology mismatch at " + i);
                    var p = new Vector3Int(x, -y - 1, 0);
                    var tile = ground.GetTile<Tile>(p);
                    if (tile == null ||
                        tile.name != PastoralGridRaster.GroundTileId(soilMask, m.groundVariant[i]))
                        throw new Exception("Native tile mismatch at " + i);
                    var center = ground.GetCellCenterWorld(p);
                    if (Vector2.Distance(center, PastoralGridV2Runtime.World(x * 32 + 16, y * 32 + 16)) >
                        .001f)
                        throw new Exception("Half-cell dual grid placement");
                    if (Math.Abs(tile.sprite.rect.width - 32) > .001 ||
                        Math.Abs(tile.sprite.pixelsPerUnit - 32) > .001)
                        throw new Exception("Native tile scaled away from32px");
                    c.groundTiles++;
                }
            if (m.highlands.Length != 0 || m.waterfall != null || m.terrainSprites.Length != 0)
                throw new Exception("Flat scope contains height geometry");
            Physics2D.SyncTransforms();
            int w = m.displayWidth;
            const int mask = 1 << 8;
            for (int i = 0; i < m.navWalkable.Length; i++)
                if (m.navWalkable[i] != 0)
                {
                    var p = PastoralGridV2Runtime.World(i % w * 16 + 8, i / w * 16 + 8);
                    var hit = Physics2D.OverlapCircle(p, .3f, mask);
                    if (hit != null)
                        throw new Exception("Navigation overlaps " + hit.name + " at " + i);
                    c.navNodes++;
                    for (int d = 0; d < 2; d++)
                        if ((m.navLinks[i] & (1 << d)) != 0)
                        {
                            var cast = Physics2D.CircleCast(p, .3f, d == 0 ? Vector2.right : Vector2.down,
                                                            .5f, mask);
                            if (cast.collider != null)
                                throw new Exception("Navigation sweep blocked at " + i + " by " +
                                                    cast.collider.name);
                            c.navSweeps++;
                        }
                }
            foreach (var b in r.FencePlan.barriers)
            {
                var p = (PastoralGridV2Runtime.World(b.a) + PastoralGridV2Runtime.World(b.b)) * .5f;
                if (Physics2D.OverlapPoint(p, mask) == null)
                    throw new Exception("Fence missing physical barrier");
            }
            foreach (var p in r.PropRenderers)
                if (p.sortingOrder != ForestDepth.Order(p.transform.position.y) + 1)
                    throw new Exception("Incorrect vegetation sort " + p.name);
            foreach (var f in r.FenceRenderers)
                if (f.sortingOrder != ForestDepth.Order(f.transform.position.y) + 1)
                    throw new Exception("Incorrect fence sort " + f.name);
            c.trees = m.validation.trees;
            c.fencePieces = r.FencePlan.pieces.Length;
            c.fenceGroups = r.FencePlan.groups.Length;
            if (c.fencePieces < 4 || c.fenceGroups < 2)
                throw new Exception("Fence composition missing");
            if (!m.validation.passed)
                throw new Exception("Core validation failed");
        }

        void Finish()
        {
            report.failures = failures.ToArray();
            report.passed = failures.Count == 0 && report.cases.Count == 12;
            File.WriteAllText(Path.Combine(outputDirectory, "unity_acceptance.json"),
                              JsonUtility.ToJson(report, true));
            Debug.Log("PASTORAL_GRID_V2_ACCEPTANCE " + report.passed + " " + outputDirectory);
        }
    }
}
