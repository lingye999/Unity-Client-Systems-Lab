using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForgottenMemories.PastoralGridV2
{
    // Native shared-corner shoreline artwork. Geometry belongs to the river
    // generator; this helper only selects unscaled source pixels and frames.
    public sealed class PastoralRiverRaster
    {
        public const int FrameCount = 6;
        public const int VariantCount = 3;
        const int TileSize = 32, GridWidth = 48, GridHeight = 36;
        readonly ForestRaster source;
        readonly Color32[] waves;
        readonly Dictionary<int, bool[]> clearPixels = new Dictionary<int, bool[]>();
        readonly Dictionary<int, bool[]> wetPixels = new Dictionary<int, bool[]>();
        readonly Dictionary<int, int[]> patterns = new Dictionary<int, int[]>();
        readonly double[,] horizontal = new double[18, 18], vertical = new double[18, 18];

        public PastoralRiverRaster(ForestRaster source)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            this.source = source;
            var texture = Resources.Load<Texture2D>("ForestMap/waves");
            if (texture == null || texture.width != 192 || texture.height != 96)
                throw new InvalidOperationException("The three native six-frame water variants are missing.");
            var raw = texture.GetPixels32();
            waves = new Color32[raw.Length];
            for (int y = 0; y < texture.height; y++)
                Array.Copy(raw, (texture.height - y - 1) * texture.width, waves, y * texture.width,
                           texture.width);
            for (int mask = 1; mask < 16; mask++)
            {
                if (mask == 5 || mask == 10)
                    continue;
                var clear = new bool[1024];
                var wet = new bool[1024];
                var frames = new Color32[FrameCount][];
                for (int f = 0; f < FrameCount; f++)
                    frames[f] = source.Pixels(NativeTileId(mask, f));
                var first = frames[0];
                for (int i = 0; i < clear.Length; i++)
                {
                    // Only open blue water is replaced. Native bank stones,
                    // wet rock, dark rim, foam and transparent land stay intact.
                    clear[i] = true;
                    for (int f = 0; f < FrameCount; f++)
                        clear[i] &= IsClearWater(frames[f][i]);
                    var c = first[i];
                    wet[i] = c.a != 0 && c.b > c.r + 10 && c.g > c.r + 10;
                }
                clearPixels.Add(mask, clear);
                wetPixels.Add(mask, wet);
            }
            // Candidate adjacency is measured across the entire six-frame
            // cycle. Spatial variation therefore need not restart every tile
            // with the same ripples or introduce a high-contrast square seam.
            for (int a = 0; a < 18; a++)
                for (int b = 0; b < 18; b++)
                {
                    int badX = 0, badY = 0;
                    for (int f = 0; f < FrameCount; f++)
                        for (int p = 0; p < TileSize; p++)
                        {
                            if (!Same(Wave(a, f, 31, p), Wave(b, f, 0, p)))
                                badX++;
                            if (!Same(Wave(a, f, p, 31), Wave(b, f, p, 0)))
                                badY++;
                        }
                    horizontal[a, b] = badX / 192.0;
                    vertical[a, b] = badY / 192.0;
                }
        }

        static bool IsClearWater(Color32 c)
        {
            return c.a == 255 && c.r <= 110 && c.g >= 125 && c.b >= 140 && c.b > c.r + 20;
        }

        static bool Same(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }

        static uint Hash(uint x)
        {
            unchecked
            {
                x ^= x >> 16;
                x *= 0x7feb352dU;
                x ^= x >> 15;
                x *= 0x846ca68bU;
                return x ^ (x >> 16);
            }
        }

        static int Mod(int value, int modulus)
        {
            int r = value % modulus;
            return r < 0 ? r + modulus : r;
        }

        Color32 Wave(int candidate, int frame, int x, int y)
        {
            int variant = candidate / FrameCount, phase = candidate % FrameCount;
            return waves[(variant * 32 + y) * 192 + Mod(frame + phase, FrameCount) * 32 + x];
        }

        int[] Pattern(int seed)
        {
            int[] pattern;
            if (patterns.TryGetValue(seed, out pattern))
                return pattern;
            pattern = new int[GridWidth * GridHeight];
            for (int y = 0; y < GridHeight; y++)
                for (int x = 0; x < GridWidth; x++)
                {
                    uint h = Hash(unchecked((uint)seed ^ (uint)x * 0x9e3779b9U ^ (uint)y * 0x85ebca6bU));
                    int wanted = (int)(h % 18U), index = y * GridWidth + x, best = 0;
                    double bestCost = double.MaxValue;
                    for (int candidate = 0; candidate < 18; candidate++)
                    {
                        double cost = (candidate == wanted ? 0 : .075);
                        if (x > 0)
                            cost += horizontal[pattern[index - 1], candidate] * .4;
                        if (y > 0)
                            cost += vertical[pattern[index - GridWidth], candidate] * .4;
                        // Avoid selecting only one convenient adjacent patch.
                        if (x > 0 && pattern[index - 1] == candidate)
                            cost += .012;
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            best = candidate;
                        }
                    }
                    pattern[index] = best;
                }
            // Runtime generation can be repeated indefinitely; retain only a
            // small working set of deterministic spatial plans.
            if (patterns.Count >= 8)
                patterns.Clear();
            patterns.Add(seed, pattern);
            return pattern;
        }

        int Candidate(int seed, int tileX, int tileY)
        {
            if (tileX < 0 || tileY < 0 || tileX >= GridWidth || tileY >= GridHeight)
                throw new ArgumentOutOfRangeException(
                    "tileX", "River display cells must belong to the 48 by 36 native grid.");
            return Pattern(seed)[tileY * GridWidth + tileX];
        }

        public int WaterVariant(int seed, int tileX, int tileY)
        {
            return Candidate(seed, tileX, tileY) / FrameCount;
        }

        public int WaterPhase(int seed, int tileX, int tileY)
        {
            return Candidate(seed, tileX, tileY) % FrameCount;
        }

        // V2 stores water bits; the source stores land bits in the same
        // clockwise NW1 NE2 SE4 SW8 order. Dry cells require no river overlay.
        public static string NativeTileId(int waterMask, int frame)
        {
            if (waterMask < 0 || waterMask > 15)
                throw new ArgumentOutOfRangeException("waterMask");
            if (waterMask == 5 || waterMask == 10)
                throw new InvalidOperationException(
                    "Repair diagonal river samples before selecting native shore artwork.");
            if (waterMask == 0)
                return null;
            return "water_" + (15 ^ waterMask).ToString("D2") + "_" + Mod(frame, FrameCount).ToString("D2");
        }

        public Color32[] TilePixels(int waterMask, int frame, int tileX, int tileY, int seed)
        {
            string id = NativeTileId(waterMask, frame);
            if (id == null)
                return new Color32[1024];
            var result = (Color32[])source.Pixels(id).Clone();
            var clear = clearPixels[waterMask];
            int candidate = Candidate(seed, tileX, tileY);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    int i = y * 32 + x;
                    if (clear[i])
                        result[i] = Wave(candidate, frame, x, y);
                }
            return result;
        }

        public bool IsWetPixel(int waterMask, int localX, int localY)
        {
            if (waterMask == 0 || localX < 0 || localY < 0 || localX >= 32 || localY >= 32)
                return false;
            if (!wetPixels.ContainsKey(waterMask))
                NativeTileId(waterMask, 0);
            return wetPixels[waterMask][localY * 32 + localX];
        }

        public bool IsOpenWaterPixel(int waterMask, int localX, int localY)
        {
            if (waterMask == 0 || localX < 0 || localY < 0 || localX >= 32 || localY >= 32)
                return false;
            if (!clearPixels.ContainsKey(waterMask))
                NativeTileId(waterMask, 0);
            return clearPixels[waterMask][localY * 32 + localX];
        }

        // Verified against tools/build_terrain_sample.py and the original
        // WaterTiles-6frames.png. Rect coordinates use its top-left origin.
        public static RectInt OriginalSourceRect(int waterMask, int frame)
        {
            NativeTileId(waterMask, frame);
            int landMask = 15 ^ waterMask, x, y;
            switch (landMask)
            {
            case 0:
                x = 352;
                y = 704;
                break;
            case 1:
                x = 256;
                y = 128;
                break;
            case 2:
                x = 480;
                y = 128;
                break;
            case 3:
                x = 704;
                y = 64;
                break;
            case 4:
                x = 480;
                y = 0;
                break;
            case 6:
                x = 480;
                y = 64;
                break;
            case 7:
                x = 480;
                y = 288;
                break;
            case 8:
                x = 256;
                y = 0;
                break;
            case 9:
                x = 256;
                y = 64;
                break;
            case 11:
                x = 256;
                y = 288;
                break;
            case 12:
                x = 704;
                y = 256;
                break;
            case 13:
                x = 256;
                y = 256;
                break;
            case 14:
                x = 480;
                y = 256;
                break;
            default:
                return new RectInt();
            }
            return new RectInt(x + Mod(frame, FrameCount) * 32, y, 32, 32);
        }
    }
}
