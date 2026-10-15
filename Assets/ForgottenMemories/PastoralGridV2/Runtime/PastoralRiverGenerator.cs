using System;
using System.Collections.Generic;
namespace ForgottenMemories.PastoralGridV2
{
    // 噪声改变河流中心和宽度；整理后的共享样本才是显示、碰撞和导航的岸线来源。
    public static class PastoralRiverGenerator
    {
        public const int BankProtectionPixels = 8;
        public const double PlayerRadiusPixels = ForestMapMetrics.PlayerRadiusPixels;
        const int W = ForestMapMetrics.Width, H = ForestMapMetrics.Height, T = ForestMapMetrics.TilePixels,
                  PW = W * T, PH = H * T;

        static double Clamp(double x, double a, double b)
        {
            return Math.Max(a, Math.Min(b, x));
        }

        static FastNoiseLite Field(int seed, int salt)
        {
            return SeededFields.Perlin(seed, unchecked((uint)salt));
        }

        static uint Hash(uint x)
        {
            return SeededFields.Hash(x);
        }

        public static RiverLayout Generate(PastoralLayout l)
        {
            var m = l.map;
            var r = new RiverLayout { waterSamples = new int[(W + 1) * (H + 1)],
                                      waterMasks = new int[W * H],
                                      centerY = new int[PW + 1],
                                      bankTop = new int[PW + 1],
                                      bankBottom = new int[PW + 1],
                                      centerline = new Point[W + 1],
                                      widths = new int[W + 1] };
            l.river = r;
            var bend = Field(m.seed, unchecked((int)0x72697672));
            var width = Field(m.seed, unchecked((int)0x77696474));
            double phase = Hash(unchecked((uint)m.seed) ^ 0x70686173U) / 4294967296.0 * Math.PI * 2;
            for (int x = 0; x <= W; x++)
            {
                double u = x / (double)W;
                double cy =
                    282 + 34 * Math.Sin(u * Math.PI * 2 + phase) + 18 * bend.GetNoise((float)(u * 2.3), .63f);
                double half = Clamp(78 + 32 * Math.Sin(u * Math.PI * 3.4 + phase * .71) +
                                        17 * width.GetNoise((float)(u * 3.1), 1.91f),
                                    48, 112);
                for (int y = 0; y <= H; y++)
                    r.waterSamples[y * (W + 1) + x] = Math.Abs(y * T - cy) <= half ? 1 : 0;
            }
            // There are no native 05/10 shoreline sprites. Repair by adding
            // connected water samples, keeping a channel rather than pinching it.
            bool changed = true;
            int rounds = 0;
            while (changed && rounds++ < W * H)
            {
                changed = false;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int mask = SampleMask(r, x, y);
                        if (mask != 5 && mask != 10)
                            continue;
                        int i = y * (W + 1) + x;
                        r.waterSamples[i] = r.waterSamples[i + 1] = r.waterSamples[i + W + 1] =
                            r.waterSamples[i + W + 2] = 1;
                        r.ambiguityCorrections++;
                        changed = true;
                    }
            }
            var a = new List<Point>();
            var b = new List<Point>();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x, mask = SampleMask(r, x, y);
                    r.waterMasks[i] = mask;
                    m.shoreMasks[i] = 15 ^ mask;
                    m.terrainBlocked[i] = mask == 0 ? 0 : 1;
                    m.waterElevation[i] = mask == 0 ? -1 : 0;
                    m.waterVariant[i] = (int)(Hash(unchecked((uint)m.seed ^ (uint)i * 131U)) % 6);
                    if (mask != 0)
                        r.wetTileCount++;
                    if (mask != 0 && mask != 15)
                        r.shoreTileCount++;
                    if (mask == 0 || mask == 15)
                        continue;
                    var corners =
                        new[] { new Point(x * T, y * T), new Point((x + 1) * T, y * T),
                                new Point((x + 1) * T, (y + 1) * T), new Point(x * T, (y + 1) * T) };
                    Point first = new Point();
                    int hits = 0;
                    for (int k = 0; k < 4; k++)
                        if (((mask >> k) & 1) != ((mask >> ((k + 1) % 4)) & 1))
                        {
                            var p = new Point((corners[k].x + corners[(k + 1) % 4].x) / 2,
                                              (corners[k].y + corners[(k + 1) % 4].y) / 2);
                            if (hits++ == 0)
                                first = p;
                            else
                            {
                                a.Add(first);
                                b.Add(p);
                            }
                        }
                }
            r.shoreA = a.ToArray();
            r.shoreB = b.ToArray();
            r.minWidthPixels = int.MaxValue;
            // Canonical per-pixel banks come from repaired native contours, so
            // no downstream system accidentally uses the pre-quantized noise.
            for (int x = 0; x <= PW; x++)
            {
                double top = double.MaxValue, bottom = double.MinValue;
                for (int i = 0; i < r.shoreA.Length; i++)
                {
                    var p = r.shoreA[i];
                    var q = r.shoreB[i];
                    if (x < Math.Min(p.x, q.x) || x > Math.Max(p.x, q.x))
                        continue;
                    if (p.x == q.x)
                    {
                        top = Math.Min(top, Math.Min(p.y, q.y));
                        bottom = Math.Max(bottom, Math.Max(p.y, q.y));
                    }
                    else
                    {
                        double y = p.y + (q.y - p.y) * (x - p.x) / (double)(q.x - p.x);
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }
                }
                if (top == double.MaxValue || bottom <= top)
                    throw new InvalidOperationException("River lost continuous boundary-to-boundary water.");
                r.bankTop[x] = (int)Math.Round(top);
                r.bankBottom[x] = (int)Math.Round(bottom);
                r.centerY[x] = (r.bankTop[x] + r.bankBottom[x]) / 2;
                int span = r.bankBottom[x] - r.bankTop[x];
                r.minWidthPixels = Math.Min(r.minWidthPixels, span);
                r.maxWidthPixels = Math.Max(r.maxWidthPixels, span);
            }
            for (int x = 0; x <= W; x++)
            {
                r.centerline[x] = new Point(x * T, r.centerY[x * T]);
                r.widths[x] = r.bankBottom[x * T] - r.bankTop[x * T];
            }
            // Populate compatibility masks from this same authoritative native
            // geometry. Their legacy 16px display is not used for rendering.
            for (int y = 0; y <= PH / 16; y++)
                for (int x = 0; x <= PW / 16; x++)
                    m.logicalLand[y * (PW / 16 + 1) + x] = ContainsWater(l, x * 16, y * 16) ? 0 : 1;
            for (int y = 0; y < PH / 16; y++)
                for (int x = 0; x < PW / 16; x++)
                {
                    int i = y * (PW / 16) + x, j = y * (PW / 16 + 1) + x;
                    m.displayShoreMasks[i] = m.logicalLand[j] | m.logicalLand[j + 1] << 1 |
                                             m.logicalLand[j + PW / 16 + 2] << 2 |
                                             m.logicalLand[j + PW / 16 + 1] << 3;
                    m.displayBlocked[i] = m.displayShoreMasks[i] == 15 ? 0 : 1;
                }
            return r;
        }

        static int SampleMask(RiverLayout r, int x, int y)
        {
            int i = y * (W + 1) + x;
            return r.waterSamples[i] | r.waterSamples[i + 1] << 1 | r.waterSamples[i + W + 2] << 2 |
                   r.waterSamples[i + W + 1] << 3;
        }

        public static Point[] WaterPolygon(PastoralLayout l, int tileX, int tileY)
        {
            if (l.river == null || tileX < 0 || tileX >= W || tileY < 0 || tileY >= H)
                return new Point[0];
            int mask = l.river.waterMasks[tileY * W + tileX];
            if (mask == 0)
                return new Point[0];
            if (mask == 5 || mask == 10)
                throw new InvalidOperationException("Unsupported diagonal shoreline.");
            var corners =
                new[] { new Point(tileX * T, tileY * T), new Point((tileX + 1) * T, tileY * T),
                        new Point((tileX + 1) * T, (tileY + 1) * T), new Point(tileX * T, (tileY + 1) * T) };
            var result = new List<Point>();
            for (int k = 0; k < 4; k++)
            {
                if (((mask >> k) & 1) != 0)
                    result.Add(corners[k]);
                if (((mask >> k) & 1) != ((mask >> ((k + 1) % 4)) & 1))
                    result.Add(new Point((corners[k].x + corners[(k + 1) % 4].x) / 2,
                                         (corners[k].y + corners[(k + 1) % 4].y) / 2));
            }
            return result.ToArray();
        }

        public static bool ContainsWater(PastoralLayout l, double x, double y)
        {
            if (l.river == null || x < 0 || x > PW || y < 0 || y > PH)
                return false;
            int a = (int)x, b = Math.Min(PW, a + 1);
            double f = x - a;
            double top = l.river.bankTop[a] * (1 - f) + l.river.bankTop[b] * f,
                   bottom = l.river.bankBottom[a] * (1 - f) + l.river.bankBottom[b] * f;
            return y >= top && y <= bottom;
        }

        public static double DistanceToWater(PastoralLayout l, double x, double y)
        {
            if (l.river == null)
                return double.PositiveInfinity;
            var r = l.river;
            double best = double.MaxValue;
            for (int i = 0; i < r.shoreA.Length; i++)
            {
                var a = r.shoreA[i];
                var b = r.shoreB[i];
                double dx = b.x - a.x, dy = b.y - a.y;
                double t = Clamp(((x - a.x) * dx + (y - a.y) * dy) / Math.Max(1, dx * dx + dy * dy), 0, 1);
                double xx = x - a.x - t * dx, yy = y - a.y - t * dy;
                best = Math.Min(best, xx * xx + yy * yy);
            }
            double d = Math.Sqrt(best);
            return ContainsWater(l, x, y) ? -d : d;
        }

        public static bool DryDisk(PastoralLayout l, double x, double y, double radius)
        {
            return l.river == null || DistanceToWater(l, x, y) > radius + BankProtectionPixels;
        }

        public static bool DrySweep(PastoralLayout l, Point a, Point b, double radius)
        {
            if (l.river == null)
                return true;
            if (ContainsWater(l, a.x, a.y) || ContainsWater(l, b.x, b.y))
                return false;
            double margin = radius + BankProtectionPixels;
            var r = l.river;
            for (int i = 0; i < r.shoreA.Length; i++)
            {
                var c = r.shoreA[i];
                var d = r.shoreB[i];
                if (Math.Max(a.x, b.x) + margin < Math.Min(c.x, d.x) ||
                    Math.Min(a.x, b.x) - margin > Math.Max(c.x, d.x) ||
                    Math.Max(a.y, b.y) + margin < Math.Min(c.y, d.y) ||
                    Math.Min(a.y, b.y) - margin > Math.Max(c.y, d.y))
                    continue;
                double abx = b.x - a.x, aby = b.y - a.y, cdx = d.x - c.x, cdy = d.y - c.y,
                       det = abx * cdy - aby * cdx;
                if (Math.Abs(det) > .000001)
                {
                    double rx = c.x - a.x, ry = c.y - a.y, u = (rx * cdy - ry * cdx) / det,
                           v = (rx * aby - ry * abx) / det;
                    if (u >= 0 && u <= 1 && v >= 0 && v <= 1)
                        return false;
                }
                double gap = Math.Min(Math.Min(PointSegment(a, c, d), PointSegment(b, c, d)),
                                      Math.Min(PointSegment(c, a, b), PointSegment(d, a, b)));
                if (gap <= margin)
                    return false;
            }
            return true;
        }

        static double PointSegment(Point p, Point a, Point b)
        {
            double dx = b.x - a.x, dy = b.y - a.y,
                   t = Clamp(((p.x - a.x) * dx + (p.y - a.y) * dy) / Math.Max(1, dx * dx + dy * dy), 0, 1);
            return Math.Sqrt(Math.Pow(p.x - a.x - t * dx, 2) + Math.Pow(p.y - a.y - t * dy, 2));
        }

        public static void AppendPorts(PastoralLayout l)
        {
            if (l.river == null)
                return;
            var r = l.river;
            var ports = new List<Port>(l.map.ports);
            ports.Add(new Port { kind = "water", edge = "W", direction = "in", offset = r.bankTop[0],
                                 width = r.bankBottom[0] - r.bankTop[0], level = 0,
                                 connectedToBoundary = true });
            ports.Add(new Port { kind = "water", edge = "E", direction = "out", offset = r.bankTop[PW],
                                 width = r.bankBottom[PW] - r.bankTop[PW], level = 0,
                                 connectedToBoundary = true });
            l.map.ports = ports.ToArray();
        }
    }
}
