using System;
using System.Collections.Generic;
namespace ForgottenMemories.PastoralGridV2
{
    // A short open rail frames a reachable rest clearing. The existing road
    // strips and grove fences retain their native source and placement rules.
    public static class ScenicFencePlanner
    {
        static double Distance(double x, double y, Point a, Point b)
        {
            double dx = b.x - a.x, dy = b.y - a.y,
                   t = Math.Max(
                       0, Math.Min(1, ((x - a.x) * dx + (y - a.y) * dy) / Math.Max(1, dx * dx + dy * dy)));
            dx = x - a.x - t * dx;
            dy = y - a.y - t * dy;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static FencePlan Generate(PastoralLayout layout, Catalog catalog)
        {
            var m = layout.map;
            var plan = OpenFencePlanner.Generate(m, catalog);
            if (m.profileIndex != 3 || m.clearings.Length == 0)
                return plan;
            var specs = new Dictionary<string, SpriteSpec>();
            foreach (var s in catalog.sprites)
                specs[s.id] = s;
            var c = m.clearings[0];
            Point approach = m.entrance;
            double nearest = double.MaxValue;
            foreach (var r in m.routes)
                if (r.kind == "branch")
                {
                    var end = r.points[r.points.Length - 1];
                    double d = (end.x - c.x) * (end.x - c.x) + (end.y - c.y) * (end.y - c.y);
                    if (d < nearest)
                    {
                        nearest = d;
                        approach = r.points[Math.Max(0, r.points.Length - 5)];
                    }
                }
            // The opening faces the approach. The back edge remains outside
            // the activity space, with at least 96px of unobstructed frontage.
            bool horizontal = Math.Abs(approach.y - c.y) >= Math.Abs(approach.x - c.x);
            int side = horizontal ? Math.Sign(c.y - approach.y) : Math.Sign(c.x - approach.x);
            if (side == 0)
                side = -1;
            for (int offset = 20; offset <= 68; offset += 16)
                foreach (int bays in new[] { 4, 3 })
                {
                    var points = new List<Point>();
                    int half = bays * 16;
                    int x = c.x, y = c.y;
                    if (horizontal)
                        y += side * (c.ry + offset);
                    else
                        x += side * (c.rx + offset);
                    // Returns point toward the clearing but leave the front open.
                    if (horizontal)
                    {
                        points.Add(new Point(x - half, y - side * 32));
                        points.Add(new Point(x - half, y));
                        for (int j = 1; j <= bays; j++)
                            points.Add(new Point(x - half + j * 32, y));
                        points.Add(new Point(x + half, y - side * 32));
                    }
                    else
                    {
                        points.Add(new Point(x - side * 32, y - half));
                        points.Add(new Point(x, y - half));
                        for (int j = 1; j <= bays; j++)
                            points.Add(new Point(x, y - half + j * 32));
                        points.Add(new Point(x - side * 32, y + half));
                    }
                    bool fits = true;
                    for (int j = 1; j < points.Count && fits; j++)
                    {
                        var a = points[j - 1];
                        var b = points[j];
                        for (int k = 0; k <= 8; k++)
                        {
                            double px = a.x + (b.x - a.x) * k / 8.0, py = a.y + (b.y - a.y) * k / 8.0;
                            if (px < 36 || py < 42 || px > m.width * 32 - 36 || py > m.height * 32 - 36 ||
                                PastoralGridGenerator.DistanceToRoad(m, px, py) < 22 ||
                                PastoralGridGenerator.IsClearing(m, px, py, -8))
                            {
                                fits = false;
                                break;
                            }
                        }
                        foreach (var p in m.props)
                            if (Distance(p.rootX, p.rootY, a, b) < specs[p.id].radiusPixels + 11)
                            {
                                fits = false;
                                break;
                            }
                        foreach (var p in plan.pieces)
                            if (Distance(p.rootX, p.rootY, a, b) < 32)
                            {
                                fits = false;
                                break;
                            }
                    }
                    if (!fits)
                        continue;
                    var pieces = new List<FencePiece>(plan.pieces);
                    var barriers = new List<FenceBarrier>(plan.barriers);
                    var groups = new List<FenceGroup>(plan.groups);
                    int group = groups.Count;
                    var masks = new int[points.Count];
                    for (int j = 1; j < points.Count; j++)
                    {
                        var a = points[j - 1];
                        var b = points[j];
                        int bit = b.x > a.x ? 2 : b.x < a.x ? 8 : b.y > a.y ? 4 : 1;
                        masks[j - 1] |= bit;
                        masks[j] |= bit == 1 ? 4 : bit == 2 ? 8 : bit == 4 ? 1 : 2;
                        barriers.Add(new FenceBarrier { a = a, b = b, radius = 4 });
                    }
                    for (int j = 0; j < points.Count; j++)
                        pieces.Add(new FencePiece { id = "flat_fence_" + masks[j].ToString("00"),
                                                        rootX = points[j].x, rootY = points[j].y,
                                                        group = group });
                    groups.Add(new FenceGroup { kind = "scenic_rest", pieces = points.Count });
                    pieces.Sort((a, b) => a.rootY != b.rootY ? a.rootY.CompareTo(b.rootY)
                                                             : a.rootX.CompareTo(b.rootX));
                    return new FencePlan { pieces = pieces.ToArray(), barriers = barriers.ToArray(),
                                               groups = groups.ToArray() };
                }
            // Some clearings have a better natural plant edge. No rail is
            // preferable to blocking their entrance or moving a tree root.
            return plan;
        }
    }
}
