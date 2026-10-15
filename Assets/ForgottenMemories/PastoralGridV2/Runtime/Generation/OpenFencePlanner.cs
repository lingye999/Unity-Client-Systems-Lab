using System;
using System.Collections.Generic;

namespace ForgottenMemories.PastoralGridV2
{
    // 短栏杆依附路边植被或开放树群；连接位由同一条折线生成，端头与转角只画一次。
    public static class OpenFencePlanner
    {
        const int T = 32;

        sealed class Candidate
        {
            public string kind;
            public List<Point> line;
            public Point center;
            public uint priority;
        }

        static uint Hash(uint n)
        {
            unchecked
            {
                n ^= n >> 16;
                n *= 0x7feb352dU;
                n ^= n >> 15;
                n *= 0x846ca68bU;
                return n ^ (n >> 16);
            }
        }

        static double Sq(double x)
        {
            return x * x;
        }

        static double Distance(Point a, Point b)
        {
            return Math.Sqrt(Sq(a.x - b.x) + Sq(a.y - b.y));
        }

        static double Segment(double x, double y, Point a, Point b)
        {
            double dx = b.x - a.x, dy = b.y - a.y,
                   t = Math.Max(
                       0, Math.Min(1, ((x - a.x) * dx + (y - a.y) * dy) / Math.Max(1, dx * dx + dy * dy)));
            return Math.Sqrt(Sq(x - a.x - t * dx) + Sq(y - a.y - t * dy));
        }

        static Point Center(List<Point> line)
        {
            int x = 0, y = 0;
            foreach (var p in line)
            {
                x += p.x;
                y += p.y;
            }
            return new Point(x / line.Count, y / line.Count);
        }

        static List<Point> Polyline(params Point[] corners)
        {
            var points = new List<Point> { corners[0] };
            for (int j = 1; j < corners.Length; j++)
            {
                var p = points[points.Count - 1];
                var end = corners[j];
                int dx = Math.Sign(end.x - p.x) * T, dy = Math.Sign(end.y - p.y) * T;
                while (p.x != end.x || p.y != end.y)
                {
                    p = new Point(p.x + dx, p.y + dy);
                    points.Add(p);
                }
            }
            return points;
        }

        static bool Fits(Map m, List<Point> line, Dictionary<string, SpriteSpec> specs,
                         List<Candidate> accepted)
        {
            int width = m.width * T, height = m.height * T;
            foreach (var p in line)
            {
                if (p.x < 36 || p.y < 42 || p.x > width - 36 || p.y > height - 36)
                    return false;
                if (Distance(p, m.entrance) < 90 || Distance(p, m.exit) < 90)
                    return false;
                if (MapGeometry.IsClearing(m, p.x, p.y, 24))
                    return false;
            }
            for (int i = 1; i < line.Count; i++)
            {
                Point a = line[i - 1], b = line[i];
                for (int s = 0; s <= 4; s++)
                {
                    double x = a.x + (b.x - a.x) * s / 4.0, y = a.y + (b.y - a.y) * s / 4.0;
                    if (MapGeometry.DistanceToRoad(m, x, y) < 20 || MapGeometry.IsClearing(m, x, y, 24))
                        return false;
                }
                foreach (var prop in m.props)
                {
                    SpriteSpec spec;
                    if (!specs.TryGetValue(prop.id, out spec))
                        continue;
                    // Vegetation crowns can occlude a fence naturally; its root
                    // and solid base cannot intersect any post or rail footprint.
                    if (Segment(prop.rootX, prop.rootY, a, b) < spec.radiusPixels + 10)
                        return false;
                }
                foreach (var group in accepted)
                    foreach (var p in group.line)
                        if (Segment(p.x, p.y, a, b) < 100)
                            return false;
            }
            return true;
        }

        static bool Related(Map m, Point center, Dictionary<string, SpriteSpec> specs, double range)
        {
            foreach (var p in m.props)
            {
                SpriteSpec s;
                if (!specs.TryGetValue(p.id, out s) ||
                    !(s.family == "tree" || s.family == "sapling" || s.family == "stump"))
                    continue;
                if (Distance(center, new Point(p.rootX, p.rootY)) < range)
                    return true;
            }
            return false;
        }

        static List<Candidate> RoadCandidates(Map m, Dictionary<string, SpriteSpec> specs,
                                              bool compact = false)
        {
            var result = new List<Candidate>();
            int ordinal = 0;
            foreach (var route in m.routes)
            {
                if (route.kind != "main")
                    continue;
                for (int i = 5; i + 6 < route.points.Length; i += compact ? 2 : 3)
                {
                    Point p = route.points[i - 3], q = route.points[i + 3], mid = route.points[i];
                    double tolerance = compact ? .42 : .27;
                    bool horizontal = Math.Abs(q.y - p.y) < Math.Abs(q.x - p.x) * tolerance;
                    bool vertical = Math.Abs(q.x - p.x) < Math.Abs(q.y - p.y) * tolerance;
                    if (!horizontal && !vertical)
                        continue;
                    uint h = Hash(unchecked((uint)m.seed ^ (uint)(i * 719) ^ 0x66656e63U));
                    int edges = compact ? 2 : 2 + (int)(h % 3), half = edges * T / 2;
                    for (int side = -1; side <= 1; side += 2)
                        for (int offset = 46; offset <= (compact ? 94 : 78); offset += 16)
                        {
                            int x = mid.x, y = mid.y;
                            var line = horizontal
                                           ? Polyline(new Point(x - half, y + side * offset),
                                                      new Point(x - half + edges * T, y + side * offset))
                                           : Polyline(new Point(x + side * offset, y - half),
                                                      new Point(x + side * offset, y - half + edges * T));
                            // Only a minority receive an outward return, never
                            // staircase diagonals or rails on both road edges.
                            if (!compact && (h & 7) == 0)
                            {
                                var end = line[line.Count - 1];
                                line.Add(horizontal ? new Point(end.x, end.y + side * T)
                                                    : new Point(end.x + side * T, end.y));
                            }
                            var c = Center(line);
                            if (!Related(m, c, specs, compact ? 230 : 180))
                                continue;
                            result.Add(new Candidate { kind = "roadside", line = line, center = c,
                                                       priority = Hash(h ^ (uint)(ordinal++ * 31)) });
                        }
                }
            }
            result.Sort((a, b) => a.priority.CompareTo(b.priority));
            return result;
        }

        static Candidate GroveCandidate(Map m, Dictionary<string, SpriteSpec> specs, List<Candidate> accepted)
        {
            var trees = new List<Prop>();
            foreach (var p in m.props)
            {
                SpriteSpec s;
                if (specs.TryGetValue(p.id, out s) && s.family == "tree" && s.radiusPixels >= 14)
                    trees.Add(p);
            }
            trees.Sort((a, b) =>
                           Hash(unchecked((uint)m.seed ^ (uint)(a.rootX * 199 + a.rootY)))
                               .CompareTo(Hash(unchecked((uint)m.seed ^ (uint)(b.rootX * 199 + b.rootY)))));
            foreach (var tree in trees)
            {
                // Search an open U around a focal tree/small grove. The open
                // side faces the nearest road; it stays at least three tiles wide.
                Point nearest = m.routes[0].points[0];
                double best = double.MaxValue;
                foreach (var route in m.routes)
                    foreach (var p in route.points)
                    {
                        double d = Distance(new Point(tree.rootX, tree.rootY), p);
                        if (d < best)
                        {
                            best = d;
                            nearest = p;
                        }
                    }
                if (best < 125 || best > 330)
                    continue;
                bool openHorizontal = Math.Abs(nearest.x - tree.rootX) > Math.Abs(nearest.y - tree.rootY);
                int sign =
                    openHorizontal ? Math.Sign(nearest.x - tree.rootX) : Math.Sign(nearest.y - tree.rootY);
                for (int size = 0; size < 3; size++)
                {
                    int left = tree.rootX - 64 - size * 16, top = tree.rootY - 64 - size * 16,
                        w = 128 + size * T, h = 96 + size * T;
                    // Corners differ by multiples of native tile spacing.
                    Point a, b, c, d;
                    if (openHorizontal && sign > 0)
                    {
                        a = new Point(left + w, top);
                        b = new Point(left, top);
                        c = new Point(left, top + h);
                        d = new Point(left + w, top + h);
                    }
                    else if (openHorizontal)
                    {
                        a = new Point(left, top);
                        b = new Point(left + w, top);
                        c = new Point(left + w, top + h);
                        d = new Point(left, top + h);
                    }
                    else if (sign > 0)
                    {
                        a = new Point(left, top + h);
                        b = new Point(left, top);
                        c = new Point(left + w, top);
                        d = new Point(left + w, top + h);
                    }
                    else
                    {
                        a = new Point(left, top);
                        b = new Point(left, top + h);
                        c = new Point(left + w, top + h);
                        d = new Point(left + w, top);
                    }
                    var u = Polyline(a, b, c, d);
                    if (Fits(m, u, specs, accepted))
                        return new Candidate { kind = "open_grove", line = u, center = Center(u) };
                    // A partial L still gives the grove an intentional edge.
                    var l = Polyline(b, c, d);
                    if (Fits(m, l, specs, accepted))
                        return new Candidate { kind = "grove_corner", line = l, center = Center(l) };
                }
            }
            return null;
        }

        public static FencePlan Generate(Map m, Catalog catalog)
        {
            if (m == null || catalog == null || catalog.sprites == null)
                throw new ArgumentException("Map and audited catalog are required.");
            var specs = new Dictionary<string, SpriteSpec>(StringComparer.Ordinal);
            foreach (var s in catalog.sprites)
                specs[s.id] = s;
            var accepted = new List<Candidate>();
            // The enclosure is the one focal fence composition. Road strips
            // are subordinate, separated and never close every tree group.
            var grove = GroveCandidate(m, specs, accepted);
            if (grove != null)
                accepted.Add(grove);
            foreach (var candidate in RoadCandidates(m, specs))
            {
                if (accepted.Count >= 4)
                    break;
                bool nearby = false;
                foreach (var other in accepted)
                    if (Distance(candidate.center, other.center) < 210)
                        nearby = true;
                if (!nearby && Fits(m, candidate.line, specs, accepted))
                    accepted.Add(candidate);
            }
            // Sparse seeds occasionally lack a long, nearly axial planting
            // edge. A short native two-bay strip may accompany a gentler bend;
            // the full road, opening and root clearance checks remain equal.
            if (accepted.Count < 2)
                foreach (var candidate in RoadCandidates(m, specs, true))
                {
                    if (accepted.Count >= 2)
                        break;
                    bool nearby = false;
                    foreach (var other in accepted)
                        if (Distance(candidate.center, other.center) < 180)
                            nearby = true;
                    if (!nearby && Fits(m, candidate.line, specs, accepted))
                        accepted.Add(candidate);
                }
            // All accepted shapes remain open. Endcaps and corners are derived
            // from their graph, so a shared corner is drawn once, never doubled.
            var pieces = new List<FencePiece>();
            var barriers = new List<FenceBarrier>();
            var groups = new List<FenceGroup>();
            for (int gid = 0; gid < accepted.Count; gid++)
            {
                var shape = accepted[gid];
                var masks = new Dictionary<long, int>();
                var positions = new Dictionary<long, Point>();
                for (int i = 1; i < shape.line.Count; i++)
                {
                    Point a = shape.line[i - 1], b = shape.line[i];
                    int bit = b.x > a.x   ? 2
                              : b.x < a.x ? 8
                              : b.y > a.y ? 4
                                          : 1,
                        opposite = bit == 1   ? 4
                                   : bit == 2 ? 8
                                   : bit == 4 ? 1
                                              : 2;
                    long ka = ((long)a.x << 32) | (uint)a.y, kb = ((long)b.x << 32) | (uint)b.y;
                    int am, bm;
                    masks.TryGetValue(ka, out am);
                    masks.TryGetValue(kb, out bm);
                    masks[ka] = am | bit;
                    masks[kb] = bm | opposite;
                    positions[ka] = a;
                    positions[kb] = b;
                    barriers.Add(new FenceBarrier { a = a, b = b, radius = 4 });
                }
                foreach (var pair in masks)
                {
                    var p = positions[pair.Key];
                    pieces.Add(new FencePiece { id = "flat_fence_" + pair.Value.ToString("00"), rootX = p.x,
                                                rootY = p.y, group = gid });
                }
                groups.Add(new FenceGroup { kind = shape.kind, pieces = masks.Count });
            }
            pieces.Sort((a, b) =>
                            a.rootY != b.rootY ? a.rootY.CompareTo(b.rootY) : a.rootX.CompareTo(b.rootX));
            return new FencePlan { pieces = pieces.ToArray(), barriers = barriers.ToArray(),
                                   groups = groups.ToArray() };
        }
    }
}
