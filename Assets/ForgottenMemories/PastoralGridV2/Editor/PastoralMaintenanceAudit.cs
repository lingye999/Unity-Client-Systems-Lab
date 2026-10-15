using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace ForgottenMemories.PastoralGridV2.Editor
{
    // Editor-only regression evidence. Does not save or bake any scene.
    public static class PastoralMaintenanceAudit
    {
        [Serializable]
        public sealed class Case
        {
            public int seed, style, props, obstacles, textures, physicsPositions, physicsSweeps;
            public bool river, passed;
            public string layoutSHA256, texturesSHA256;
            public double editorGenerationMilliseconds;
        }

        [Serializable]
        public sealed class Result
        {
            public bool passed;
            public string scope, error;
            public Case[] cases;
        }

        static string Digest(byte[] bytes)
        {
            using (var hash = SHA256.Create())
            {
                return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            }
        }

        [Serializable]
        public sealed class SavedSceneCase
        {
            public string path;
            public int roots, sprites, tileTypes, dependencies;
            public bool passed;
        }

        [Serializable]
        public sealed class SavedSceneResult
        {
            public bool passed;
            public string error;
            public List<SavedSceneCase> scenes = new List<SavedSceneCase>();
        }

        // Load the retained scenes without regenerating, rebaking or saving them.
        public static void CheckSavedScenes()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Requires batch mode.");
            string output = null;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-forestMaintenanceOutput")
                    output = args[i + 1];
            if (string.IsNullOrEmpty(output))
                throw new ArgumentException("Missing -forestMaintenanceOutput");
            var result = new SavedSceneResult();
            try
            {
                foreach (string name in new[] { "RiverWoodlandV2" })
                {
                    var item = new SavedSceneCase { path = PastoralGridV2Editor.Root + "/Scenes/" + name +
                                                           ".unity" };
                    var scene = EditorSceneManager.OpenScene(item.path, OpenSceneMode.Single);
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        item.roots++;
                        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                                    transform.gameObject) != 0)
                                throw new InvalidOperationException("Missing script: " + transform.name);
                        foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                        {
                            if (renderer.sprite == null || renderer.sprite.texture == null)
                                throw new InvalidOperationException("Missing sprite: " + renderer.name);
                            item.sprites++;
                        }
                        foreach (var map in root.GetComponentsInChildren<Tilemap>(true))
                        {
                            var tiles = new TileBase[map.GetUsedTilesCount()];
                            int count = map.GetUsedTilesNonAlloc(tiles);
                            item.tileTypes += count;
                            for (int i = 0; i < count; i++)
                            {
                                if (tiles[i] == null)
                                    throw new InvalidOperationException("Missing tile: " + map.name);
                                var tile = tiles[i] as Tile;
                                if (tile != null && tile.sprite == null)
                                    throw new InvalidOperationException("Missing tile sprite.");
                                var river = tiles[i] as PastoralRiverTile;
                                if (river != null)
                                {
                                    if (river.frames == null ||
                                        river.frames.Length != PastoralRiverRaster.FrameCount)
                                        throw new InvalidOperationException("Missing river frames.");
                                    foreach (var frame in river.frames)
                                        if (frame == null)
                                            throw new InvalidOperationException("Missing river sprite.");
                                }
                            }
                        }
                    }
                    foreach (string dependency in AssetDatabase.GetDependencies(item.path, true))
                    {
                        if (!File.Exists(dependency))
                            throw new FileNotFoundException("Missing scene dependency", dependency);
                        item.dependencies++;
                    }
                    item.passed = true;
                    result.scenes.Add(item);
                }
                result.passed = result.scenes.Count == 1;
            }
            catch (Exception e)
            {
                result.error = e.ToString();
                Debug.LogException(e);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(result, true));
            EditorApplication.Exit(result.passed ? 0 : 2);
        }

        static string TextureDigest(PastoralGridV2Runtime runtime, out int count)
        {
            count = 0;
            using (var hash = SHA256.Create())
            {
                foreach (var asset in runtime.NewAssets)
                {
                    var texture = asset as Texture2D;
                    if (texture == null)
                        continue;
                    count++;
                    var label = Encoding.UTF8.GetBytes(texture.name + "|" + texture.width + "|" +
                                                       texture.height + "\n");
                    hash.TransformBlock(label, 0, label.Length, label, 0);
                    var pixels = texture.GetPixels32();
                    var bytes = new byte[pixels.Length * 4];
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        bytes[i * 4] = pixels[i].r;
                        bytes[i * 4 + 1] = pixels[i].g;
                        bytes[i * 4 + 2] = pixels[i].b;
                        bytes[i * 4 + 3] = pixels[i].a;
                    }
                    hash.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
                }
                hash.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        internal static void PhysicsCheck(PastoralGridV2Runtime runtime, Case record)
        {
            var map = runtime.Layout;
            var overlaps = new Collider2D[16];
            var hits = new RaycastHit2D[16];
            int[] dx = { 1, 0, -1, 0 }, dy = { 0, 1, 0, -1 };
            for (int i = 0; i < map.navWalkable.Length; i++)
            {
                if (map.navWalkable[i] == 0)
                    continue;
                var a =
                    PastoralGridV2Runtime.World(i % map.displayWidth * 16 + 8, i / map.displayWidth * 16 + 8);
                record.physicsPositions++;
                if (Physics2D.OverlapCircleNonAlloc(a, 9.6f / 32f, overlaps, 1 << 8) != 0)
                    throw new InvalidOperationException("Physics overlap at navigation cell " + i);
                for (int d = 0; d < 4; d++)
                {
                    if ((map.navLinks[i] & (1 << d)) == 0)
                        continue;
                    var delta = new Vector2(dx[d] * .5f, -dy[d] * .5f);
                    record.physicsSweeps++;
                    if (Physics2D.CircleCastNonAlloc(a, 9.6f / 32f, delta.normalized, hits, delta.magnitude,
                                                     1 << 8) != 0)
                        throw new InvalidOperationException("Physics sweep at navigation cell " + i);
                }
            }
        }

        public static void Run()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Maintenance audit requires batch mode.");
            string output = null;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-forestMaintenanceOutput")
                    output = args[i + 1];
            if (string.IsNullOrEmpty(output))
                throw new ArgumentException("Missing -forestMaintenanceOutput");
            var result =
                new Result { scope = "Unity Editor batch: generated texture bytes, layout and fence " +
                                     "identity, actual Physics2D circles and sweeps, invalid seed and " +
                                     "overlay lifetime. No camera capture or Player performance benchmark." };
            var records = new List<Case>();
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                foreach (bool river in new[] { false, true })
                    foreach (int style in new[] { 0, 3 })
                    {
                        var root = new GameObject("Maintenance probe");
                        try
                        {
                            var runtime = root.AddComponent<PastoralGridV2Runtime>();
                            runtime.seed = 202;
                            runtime.style = style;
                            runtime.withRiver = river;
                            runtime.showControls = false;
                            runtime.Regenerate();
                            var item =
                                new Case { seed = 202,
                                           style = style,
                                           river = river,
                                           props = runtime.Layout.props.Length,
                                           obstacles = runtime.Obstacles.Count,
                                           editorGenerationMilliseconds = runtime.GenerationMilliseconds };
                            item.layoutSHA256 =
                                Digest(Encoding.UTF8.GetBytes(JsonUtility.ToJson(runtime.GridLayout) +
                                                              JsonUtility.ToJson(runtime.FencePlan)));
                            item.texturesSHA256 = TextureDigest(runtime, out item.textures);
                            PhysicsCheck(runtime, item);
                            var generated = runtime.Generated;
                            int owned = runtime.NewAssets.Count;
                            if (runtime.TryGenerateText("invalid seed") || runtime.Generated != generated)
                                throw new InvalidOperationException("Invalid seed replaced the map.");
                            runtime.SetDiagnostics(1);
                            runtime.SetDiagnostics(0);
                            if (runtime.NewAssets.Count != owned)
                                throw new InvalidOperationException("Diagnostic resources leaked.");
                            item.passed = true;
                            records.Add(item);
                            Debug.Log("MAINTENANCE_CASE river=" + river + " style=" + style +
                                      " textures=" + item.textures);
                        }
                        finally
                        {
                            Object.DestroyImmediate(root);
                        }
                    }
                result.passed = true;
            }
            catch (Exception e)
            {
                result.error = e.ToString();
                Debug.LogException(e);
            }
            result.cases = records.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(result, true));
            EditorApplication.Exit(result.passed ? 0 : 2);
        }
    }
}
