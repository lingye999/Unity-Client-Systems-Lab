using System;

namespace ForgottenMemories.PastoralGridV2
{
    /// <summary>道路距离与空地边界查询。坐标沿用原始 PNG 的左上角像素坐标。</summary>
    public static class MapGeometry
    {
        // 当前有限地图固定为 48×36 个 32px 瓦片，与生成器保持一致。
        const int PW = 48 * 32, PH = 36 * 32;

        static double Clamp(double n, double a, double b)
        {
            return Math.Max(a, Math.Min(b, n));
        }

        internal static double Length(double x, double y)
        {
            return Math.Sqrt(x * x + y * y);
        }

        internal static double SegDistance(double x, double y, Point a, Point b)
        {
            double dx = b.x - a.x, dy = b.y - a.y,
                   t = Clamp(((x - a.x) * dx + (y - a.y) * dy) / Math.Max(1, dx * dx + dy * dy), 0, 1);
            return Length(x - a.x - t * dx, y - a.y - t * dy);
        }

        static double RawRoadDistance(Map m, double x, double y)
        {
            double best = double.MaxValue;
            foreach (var r in m.routes)
                for (int i = 1; i < r.points.Length; i++)
                    best = Math.Min(best, SegDistance(x, y, r.points[i - 1], r.points[i]) -
                                              Math.Max(r.radii[i - 1], r.radii[i]));
            return best;
        }

        public static double DistanceToRoad(Map m, double x, double y)
        {
            if (x < 0 || y < 0 || x >= PW || y >= PH)
                return -1000;
            if (m.routeClearance == null)
                return RawRoadDistance(m, x, y) - 6;
            return m.routeClearance[((int)y / 8) * (PW / 8) + (int)x / 8];
        }

        public static bool IsClearing(Map m, double x, double y, double margin = 0)
        {
            foreach (var c in m.clearings)
            {
                if (Math.Abs(x - c.x) > c.rx + margin || Math.Abs(y - c.y) > c.ry + margin)
                    continue;
                bool inside = false;
                for (int i = 0, j = c.outline.Length - 1; i < c.outline.Length; j = i++)
                {
                    Point a = c.outline[j], b = c.outline[i];
                    if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x)
                        inside = !inside;
                    if (margin > 0 && SegDistance(x, y, a, b) < margin)
                        return true;
                }
                if (inside)
                    return true;
            }
            return false;
        }
    }
}
