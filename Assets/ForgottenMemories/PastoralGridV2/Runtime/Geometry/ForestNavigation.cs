using System;
using System.Collections.Generic;

namespace ForgottenMemories.PastoralGridV2
{
    /// <summary>依据根部和栏杆实体重建四向导航；水岸阻挡由上层生成器继续叠加。</summary>
    public static class ForestNavigation
    {
        const int PW = 48 * 32, PH = 36 * 32, STEP = 16, NW = PW / STEP, NH = PH / STEP;
        const double PLAYER = 9.6;
        static readonly int[] DX = { 1, 0, -1, 0 }, DY = { 0, 1, 0, -1 };

        static Dictionary<string, SpriteSpec> Specs(Catalog catalog)
        {
            var d = new Dictionary<string, SpriteSpec>(StringComparer.Ordinal);
            foreach (var s in catalog.sprites)
                d.Add(s.id, s);
            return d;
        }

        static bool PointClear(Map m, Dictionary<string, SpriteSpec> specs, double x, double y, double radius,
                               FenceBarrier[] barriers)
        {
            if (x - radius < 0 || y - radius < 0 || x + radius > PW || y + radius > PH)
                return false;
            foreach (var p in m.props)
            {
                if (p.level != 0)
                    continue;
                var s = specs[p.id];
                if (s.family == "fence")
                {
                    if (Math.Abs(x - p.rootX) < s.width * .5 + radius && Math.Abs(y - p.rootY) < 4 + radius)
                        return false;
                }
                else if (MapGeometry.Length(x - p.rootX, y - p.rootY) < s.radiusPixels + radius)
                    return false;
            }
            if (barriers != null)
                foreach (var b in barriers)
                    if (MapGeometry.SegDistance(x, y, b.a, b.b) <= b.radius + radius)
                        return false;
            return true;
        }

        static double SegmentSegmentDistance(Point a, Point b, Point c, Point d)
        {
            double abx = b.x - a.x, aby = b.y - a.y, cdx = d.x - c.x, cdy = d.y - c.y,
                   det = abx * cdy - aby * cdx;
            if (Math.Abs(det) > .00001)
            {
                double rx = c.x - a.x, ry = c.y - a.y, u = (rx * cdy - ry * cdx) / det,
                       v = (rx * aby - ry * abx) / det;
                if (u >= 0 && u <= 1 && v >= 0 && v <= 1)
                    return 0;
            }
            return Math.Min(
                Math.Min(MapGeometry.SegDistance(a.x, a.y, c, d), MapGeometry.SegDistance(b.x, b.y, c, d)),
                Math.Min(MapGeometry.SegDistance(c.x, c.y, a, b), MapGeometry.SegDistance(d.x, d.y, a, b)));
        }

        static bool SweepClear(Map m, Dictionary<string, SpriteSpec> specs, Point a, Point b,
                               FenceBarrier[] barriers)
        {
            foreach (var p in m.props)
            {
                if (p.level != 0)
                    continue;
                var s = specs[p.id];
                if (s.family == "fence")
                {
                    var c = new Point(p.rootX - s.width / 2, p.rootY);
                    var d = new Point(p.rootX + s.width / 2, p.rootY);
                    if (SegmentSegmentDistance(a, b, c, d) <= PLAYER + 4)
                        return false;
                }
                else if (MapGeometry.SegDistance(p.rootX, p.rootY, a, b) <= s.radiusPixels + PLAYER)
                    return false;
            }
            if (barriers != null)
                foreach (var fence in barriers)
                    if (SegmentSegmentDistance(a, b, fence.a, fence.b) <= PLAYER + fence.radius)
                        return false;
            return true;
        }

        public static void Rebuild(Map m, Catalog catalog, FenceBarrier[] barriers = null)
        {
            var specs = Specs(catalog);
            m.navWalkable = new int[NW * NH];
            m.navLinks = new int[NW * NH];
            for (int y = 0; y < NH; y++)
                for (int x = 0; x < NW; x++)
                    m.navWalkable[y * NW + x] =
                        PointClear(m, specs, x * STEP + 8, y * STEP + 8, PLAYER, barriers) ? 1 : 0;
            for (int i = 0; i < m.navWalkable.Length; i++)
            {
                if (m.navWalkable[i] == 0)
                    continue;
                var a = new Point(i % NW * STEP + 8, i / NW * STEP + 8);
                for (int d = 0; d < 4; d++)
                {
                    int x = i % NW + DX[d], y = i / NW + DY[d];
                    if (x < 0 || y < 0 || x >= NW || y >= NH || m.navWalkable[y * NW + x] == 0)
                        continue;
                    var b = new Point(x * STEP + 8, y * STEP + 8);
                    if (SweepClear(m, specs, a, b, barriers))
                        m.navLinks[i] |= 1 << d;
                }
            }
        }

        public static Report Validate(Map m, Catalog catalog)
        {
            var errors = new List<string>();
            var specs = Specs(catalog);
            var seen = new bool[NW * NH];
            var queue = new Queue<int>();
            int start = m.entrance.y / STEP * NW + m.entrance.x / STEP;
            if (m.navWalkable[start] == 1)
            {
                queue.Enqueue(start);
                seen[start] = true;
            }
            int reached = 0;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                reached++;
                for (int d = 0; d < 4; d++)
                    if ((m.navLinks[i] & (1 << d)) != 0)
                    {
                        int j = (i / NW + DY[d]) * NW + i % NW + DX[d];
                        if (!seen[j])
                        {
                            seen[j] = true;
                            queue.Enqueue(j);
                        }
                    }
            }
            var goals = new List<Point>(m.collectibles) { m.exit };
            foreach (var p in goals)
                if (!seen[p.y / STEP * NW + p.x / STEP])
                    errors.Add("Activity goal unreachable");
            int trees = 0;
            foreach (var p in m.props)
            {
                var s = specs[p.id];
                if (s.family == "tree")
                    trees++;
                if (p.level != 0)
                    errors.Add("Flat scene contains elevated prop");
                if (MapGeometry.DistanceToRoad(m, p.rootX, p.rootY) < s.radiusPixels + PLAYER)
                    errors.Add("Prop root intersects route");
                if (MapGeometry.IsClearing(m, p.rootX, p.rootY, s.radiusPixels))
                    errors.Add("Prop root intersects activity clearing");
            }
            if (trees < 20 || m.groves.Length < 4)
                errors.Add("Insufficient woodland structure");
            if (m.waterfall != null || m.highlands.Length != 0 || m.terrainSprites.Length != 0)
                errors.Add("Flat scene contains height or waterfall");
            int main = 0, trails = 0;
            foreach (var r in m.routes)
            {
                if (r.kind == "main")
                    main++;
                else
                    trails++;
                foreach (var p in r.points)
                    if (p.x < 30 || p.x > PW - 30 || p.y < 30 || p.y > PH - 30)
                        errors.Add("Route leaves inner map bounds");
            }
            return new Report { passed = errors.Count == 0,  failures = errors.ToArray(),
                                reachableNavCells = reached, trees = trees,
                                groves = m.groves.Length,    props = m.props.Length,
                                mainRoutes = main,           trails = trails };
        }
    }
}
