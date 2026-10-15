using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForgottenMemories.PastoralGridV2
{
    // Native 32 px source art: logical soil samples are inverted to the
    // source's grass-corner masks. This class never paints the road geometry.
    public sealed class PastoralGridRaster
    {
        readonly ForestRaster source;
        readonly Color32[] understory;
        readonly int shadeWidth, shadeHeight;
        readonly List<Stone> stones = new List<Stone>();
        readonly List<Stone> gravel = new List<Stone>();
        readonly List<Stone> stepping = new List<Stone>();
        readonly Dictionary<int, bool[]> grassPixels = new Dictionary<int, bool[]>();
        readonly Dictionary<int, bool[]> soilPixels = new Dictionary<int, bool[]>();

        sealed class Stone
        {
            public int width, height, area;
            public Color32[] pixels;
        }

        public PastoralGridRaster(ForestRaster source)
        {
            if (source == null)
                throw new ArgumentNullException("source");
            this.source = source;
            // Native edge pixels contain both grass and brown soil. Test them
            // once so forest-floor stamps and whole stones follow that edge.
            for (int mask = 0; mask < 16; mask++)
            {
                if (mask == 5 || mask == 10)
                    continue;
                var p = source.Pixels(GroundTileId(mask, 0));
                var grass = new bool[1024];
                var soil = new bool[1024];
                for (int i = 0; i < p.Length; i++)
                {
                    grass[i] = mask == 0 || mask != 15 && p[i].g >= p[i].r;
                    soil[i] = mask == 15 || mask != 0 && p[i].r - p[i].g > 20 && p[i].r > 80;
                }
                grassPixels.Add(mask, grass);
                soilPixels.Add(mask, soil);
            }
            var shade = Resources.Load<Texture2D>("ForestMap/understory");
            if (shade == null)
                throw new InvalidOperationException("Native understory artwork is missing.");
            // The original crop has an open right edge. Reusing its mirrored
            // native half closes that contour without cutting a square patch.
            int nativeWidth = shade.width;
            shadeWidth = nativeWidth * 2;
            shadeHeight = shade.height;
            var raw = shade.GetPixels32();
            understory = new Color32[shadeWidth * shadeHeight];
            for (int y = 0; y < shadeHeight; y++)
                for (int x = 0; x < shadeWidth; x++)
                {
                    int sx = x < nativeWidth ? x : shadeWidth - x - 1;
                    understory[y * shadeWidth + x] = raw[(shadeHeight - 1 - y) * nativeWidth + sx];
                }
            var paving = Resources.Load<Texture2D>("ForestMap/paving_dense");
            if (paving == null)
                throw new InvalidOperationException("Audited native paving source is missing.");
            ExtractWholeStones(paving);
            if (stones.Count == 0)
                throw new InvalidOperationException("Native paving contains no whole selectable stones.");
            // Grass walks need legible stepping surfaces rather than the
            // smallest specks or elongated source clusters. Original artwork
            // stays at its native orientation and scale.
            foreach (var stone in stones)
                if (stone.width >= 9 && stone.width <= 18 && stone.height >= 6 && stone.height <= 16 &&
                    stone.area >= 50)
                    stepping.Add(stone);
            if (stepping.Count == 0)
                throw new InvalidOperationException(
                    "Native paving has no complete grass-walk stepping stones.");
            // dirt_0 is plain native brown. dirt_1 includes three small whole
            // pebble islands and a large cropped patch. Extract only complete
            // non-background components; never repeat the full circular patch.
            var dirt = (Color32[])source.Pixels("dirt_1").Clone();
            var brown = source.Pixels("grass_00")[0];
            for (int i = 0; i < dirt.Length; i++)
                if (dirt[i].r == brown.r && dirt[i].g == brown.g && dirt[i].b == brown.b &&
                    dirt[i].a == brown.a)
                    dirt[i] = new Color32();
            ExtractWholeComponents(dirt, 32, 32, gravel, 3, 80);
            if (gravel.Count == 0)
                throw new InvalidOperationException(
                    "Native dirt artwork contains no whole selectable gravel.");
        }

        public static string GroundTileId(int soilMask, int variant)
        {
            if (soilMask < 0 || soilMask > 15)
                throw new ArgumentOutOfRangeException("soilMask");
            if (soilMask == 5 || soilMask == 10)
                throw new InvalidOperationException(
                    "Native grass/soil kit has no diagonal 05/10: repair logical samples before rendering.");
            if (soilMask == 0)
                return "grass_base";
            if (soilMask == 15)
                return "grass_00";
            return "grass_" + (15 ^ soilMask).ToString("D2");
        }

        public static string GroundTileId(int soilMask)
        {
            return GroundTileId(soilMask, 0);
        }

        public Color32[] Pixels(string id)
        {
            return source.Pixels(id);
        }

        public int WholeStoneCount
        {
            get {
                return stones.Count;
            }
        }
        public int WholeGravelCount
        {
            get {
                return gravel.Count;
            }
        }
        public int SteppingStoneCount
        {
            get {
                return stepping.Count;
            }
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

        bool MaterialAt(PastoralLayout l, int x, int y, bool soil)
        {
            int w = l.map.width * 32, h = l.map.height * 32;
            if (x < 0 || y < 0 || x >= w || y >= h)
                return false;
            int mask = l.soilMasks[(y / 32) * l.map.width + x / 32];
            return (soil ? soilPixels[mask] : grassPixels[mask])[(y % 32) * 32 + x % 32];
        }

        public bool IsSoilPixel(PastoralLayout l, int x, int y)
        {
            return MaterialAt(l, x, y, true);
        }

        public bool IsPavingPixel(PastoralLayout l, int x, int y)
        {
            if (MaterialAt(l, x, y, true) && PastoralGridGenerator.DistanceToRoad(l.map, x, y) <= -5)
                return true;
            int route = GrassRouteAt(l, x, y);
            return route >= 0 && GrassWalkPixel(l, route, x, y);
        }

        static double RouteClearance(Route route, double x, double y)
        {
            double nearest = double.MaxValue;
            for (int i = 1; i < route.points.Length; i++)
            {
                var a = route.points[i - 1];
                var b = route.points[i];
                double dx = b.x - a.x, dy = b.y - a.y;
                double t = Math.Max(
                    0, Math.Min(1, ((x - a.x) * dx + (y - a.y) * dy) / Math.Max(1, dx * dx + dy * dy)));
                double px = x - a.x - t * dx, py = y - a.y - t * dy;
                double radius = route.radii[i - 1] + (route.radii[i] - route.radii[i - 1]) * t;
                nearest = Math.Min(nearest, Math.Sqrt(px * px + py * py) - radius);
            }
            return nearest;
        }

        static int GrassRouteAt(PastoralLayout l, double x, double y)
        {
            int index = -1;
            double nearest = 0;
            for (int i = 0; i < l.map.routes.Length; i++)
            {
                if (!PastoralGridGenerator.IsGrassRoute(l, i))
                    continue;
                double d = RouteClearance(l.map.routes[i], x, y);
                if (d < nearest)
                {
                    nearest = d;
                    index = i;
                }
            }
            return index;
        }

        bool GrassWalkPixel(PastoralLayout l, int route, int x, int y)
        {
            if (!MaterialAt(l, x, y, false) || RouteClearance(l.map.routes[route], x, y) > -2)
                return false;
            int nx = x / l.map.navStep, ny = y / l.map.navStep;
            int index = ny * l.map.displayWidth + nx;
            return l.map.navWalkable == null ||
                   index >= 0 && index < l.map.navWalkable.Length && l.map.navWalkable[index] != 0;
        }

        void ExtractWholeStones(Texture2D texture)
        {
            int w = texture.width, h = texture.height;
            var raw = texture.GetPixels32();
            var pixels = new Color32[raw.Length];
            for (int y = 0; y < h; y++)
                Array.Copy(raw, (h - y - 1) * w, pixels, y * w, w);
            ExtractWholeComponents(pixels, w, h, stones, 5, 400);
        }

        static void ExtractWholeComponents(Color32[] pixels, int w, int h, List<Stone> output, int minArea,
                                           int maxArea)
        {
            var seen = new bool[pixels.Length];
            for (int start = 0; start < pixels.Length; start++)
            {
                if (seen[start] || pixels[start].a == 0)
                    continue;
                var component = new List<int>();
                var queue = new Queue<int>();
                int left = w, right = 0, top = h, bottom = 0;
                bool touchesEdge = false;
                queue.Enqueue(start);
                seen[start] = true;
                while (queue.Count > 0)
                {
                    int i = queue.Dequeue(), x = i % w, y = i / w;
                    component.Add(i);
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                    touchesEdge |= x == 0 || y == 0 || x == w - 1 || y == h - 1;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + (d == 0 ? -1 : d == 1 ? 1 : 0), ny = y + (d == 2 ? -1 : d == 3 ? 1 : 0);
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h)
                            continue;
                        int ni = ny * w + nx;
                        if (!seen[ni] && pixels[ni].a > 0)
                        {
                            seen[ni] = true;
                            queue.Enqueue(ni);
                        }
                    }
                }
                // Boundary-touching components could be cut source stones.
                if (touchesEdge || component.Count < minArea || component.Count > maxArea ||
                    right - left < 2 || bottom - top < 2)
                    continue;
                var s =
                    new Stone { width = right - left + 1, height = bottom - top + 1, area = component.Count };
                s.pixels = new Color32[s.width * s.height];
                foreach (int i in component)
                    s.pixels[(i / w - top) * s.width + i % w - left] = pixels[i];
                output.Add(s);
            }
        }

        public Color32[] Understory(PastoralLayout l)
        {
            var m = l.map;
            int w = m.width * 32, h = m.height * 32;
            var result = new Color32[w * h];
            if (m.groves == null)
                return result;
            for (int group = 0; group < m.groves.Length; group++)
            {
                var grove = m.groves[group];
                if (grove.accepted < 2)
                    continue;
                var centers = new List<Point> { new Point(grove.x, grove.y + 10) };
                foreach (var p in m.props)
                {
                    if (p.group != group || source.specs[p.id].family != "tree")
                        continue;
                    var center = new Point((grove.x + p.rootX) / 2, (grove.y + p.rootY) / 2 + 8);
                    bool close = false;
                    foreach (var other in centers)
                        if ((center.x - other.x) * (center.x - other.x) +
                                (center.y - other.y) * (center.y - other.y) <
                            48 * 48)
                            close = true;
                    if (!close)
                        centers.Add(center);
                    if (centers.Count == 4)
                        break;
                }
                foreach (var center in centers)
                {
                    int left = center.x - shadeWidth / 2, top = center.y - shadeHeight / 2;
                    for (int sy = 0; sy < shadeHeight; sy++)
                        for (int sx = 0; sx < shadeWidth; sx++)
                        {
                            var pixel = understory[sy * shadeWidth + sx];
                            if (pixel.a == 0)
                                continue;
                            int x = left + sx, y = top + sy;
                            // Soil is already rejected by the visible native
                            // material pixels. A second, cached navigation buffer
                            // cuts a straight strip out of the forest floor away
                            // from the actual road edge, so it must not clip art.
                            if (!MaterialAt(l, x, y, false) || PastoralGridGenerator.IsClearing(m, x, y, 5))
                                continue;
                            int i = y * w + x;
                            result[i] = ForestRaster.Over(result[i], pixel);
                        }
                }
            }
            return result;
        }

        public Color32[] Gravel(PastoralLayout l)
        {
            var m = l.map;
            int w = m.width * 32, h = m.height * 32;
            var result = new Color32[w * h];
            var occupied = new bool[result.Length];
            var anchors = new List<Point>();
            // Small concentrated remnants are tied to junction candidates.
            // Each selected stone is copied whole, never clipped to a circle.
            foreach (var p in m.pavers)
            {
                var center = new Point(p.x + 16, p.y + 16);
                bool close = false;
                if (GrassRouteAt(l, center.x, center.y) >= 0)
                    continue;
                foreach (var a in anchors)
                    if (Math.Abs(a.x - center.x) + Math.Abs(a.y - center.y) < 110)
                        close = true;
                if (close)
                    continue;
                anchors.Add(center);
                uint phase = Hash(unchecked((uint)m.seed ^ (uint)(center.x * 397 + center.y * 71)));
                StampCluster(l, result, occupied, center, 4 + (int)(phase % 4), phase, 22, stones);
                StampCluster(l, result, occupied, center, 9 + (int)(phase % 7), Hash(phase), 30, gravel);
            }
            for (int routeIndex = 0; routeIndex < m.routes.Length; routeIndex++)
            {
                var route = m.routes[routeIndex];
                if (PastoralGridGenerator.IsGrassRoute(l, routeIndex))
                    continue;
                int stride = route.kind == "branch" ? 11 : 7;
                int phaseOffset =
                    (int)(Hash(unchecked((uint)m.seed ^ (uint)route.points[0].x)) % (uint)stride);
                for (int i = 2 + phaseOffset; i < route.points.Length - 2; i += stride)
                {
                    var center = route.points[i];
                    uint phase = Hash(unchecked((uint)m.seed ^ (uint)(center.x * 719 + center.y * 1877)));
                    // Alternate sparse sections and bare sections, avoiding a
                    // uniform dotted line or a carpet of identical paving.
                    if (phase % 5 < 2)
                        continue;
                    bool close = false;
                    foreach (var a in anchors)
                        if (Math.Abs(a.x - center.x) + Math.Abs(a.y - center.y) < 85)
                            close = true;
                    if (close)
                        continue;
                    anchors.Add(center);
                    int count = route.kind == "branch" ? 1 : 1 + (int)(phase % 3);
                    StampCluster(l, result, occupied, center, count, phase, 15, stones);
                    StampCluster(l, result, occupied, center,
                                 route.kind == "branch" ? 3 : 4 + (int)(phase % 5), Hash(phase), 20, gravel);
                }
            }
            GrassPaving(l, result, occupied);
            return result;
        }

        void GrassPaving(PastoralLayout l, Color32[] target, bool[] occupied)
        {
            var anchors = new List<Point>();
            for (int routeIndex = 0; routeIndex < l.map.routes.Length; routeIndex++)
            {
                if (!PastoralGridGenerator.IsGrassRoute(l, routeIndex))
                    continue;
                var route = l.map.routes[routeIndex];
                double[] lengths = new double[route.points.Length];
                for (int i = 1; i < lengths.Length; i++)
                {
                    int dx = route.points[i].x - route.points[i - 1].x,
                        dy = route.points[i].y - route.points[i - 1].y;
                    lengths[i] = lengths[i - 1] + Math.Sqrt(dx * dx + dy * dy);
                }
                uint salt = Hash(unchecked((uint)l.map.seed ^ (uint)(routeIndex + 1) * 0x3c6ef35fU));
                double distance = 18 + salt % 15;
                int sequence = 0, segment = 1;
                while (distance < lengths[lengths.Length - 1] - 14)
                {
                    uint phase = Hash(salt + (uint)sequence++ * 0x9e3779b9U);
                    while (segment < lengths.Length - 1 && lengths[segment] < distance)
                        segment++;
                    double t = (distance - lengths[segment - 1]) /
                               Math.Max(1, lengths[segment] - lengths[segment - 1]);
                    var a = route.points[segment - 1];
                    var b = route.points[segment];
                    var center = new Point((int)Math.Round(a.x + (b.x - a.x) * t),
                                           (int)Math.Round(a.y + (b.y - a.y) * t));
                    bool close = false;
                    foreach (var old in anchors)
                        if ((old.x - center.x) * (old.x - center.x) +
                                (old.y - center.y) * (old.y - center.y) <
                            24 * 24)
                            close = true;
                    // Groups establish a readable route. Occasional omitted
                    // groups preserve bare grass intervals, not white confetti.
                    if (!close && phase % 11 != 0)
                    {
                        anchors.Add(center);
                        StampCluster(l, target, occupied, center, 2 + (int)(phase % 2), phase, 9, stepping,
                                     routeIndex);
                        if (phase % 4 == 0)
                            StampCluster(l, target, occupied, center, 1 + (int)(phase % 3), Hash(phase), 15,
                                         gravel, routeIndex);
                    }
                    distance += 43 + phase % 20;
                }
            }
            // A small maintained approach at the scenic rest clearing. The
            // stones remain flat decorations and never block the open entrance.
            if (l.map.clearings.Length == 0)
                return;
            var rest = l.map.clearings[0];
            int restRoute = GrassRouteAt(l, rest.x, rest.y);
            if (restRoute < 0)
                return;
            uint restPhase = Hash(unchecked((uint)l.map.seed ^ 0x72657374U));
            StampCluster(l, target, occupied, new Point(rest.x, rest.y), 8, restPhase, 21, stepping,
                         restRoute);
            StampCluster(l, target, occupied, new Point(rest.x, rest.y), 4, Hash(restPhase), 24, gravel,
                         restRoute);
        }

        void StampCluster(PastoralLayout l, Color32[] target, bool[] occupied, Point center, int count,
                          uint phase, int spread, List<Stone> fragments, int grassRoute = -1)
        {
            int w = l.map.width * 32, h = l.map.height * 32;
            for (int n = 0; n < count; n++)
            {
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    phase = Hash(phase + 0x9e3779b9U);
                    var s = fragments[(int)(phase % (uint)fragments.Count)];
                    int x = center.x + (int)((phase >> 8) % (uint)(spread * 2 + 1)) - spread - s.width / 2;
                    int y = center.y + (int)((phase >> 17) % (uint)(spread * 2 + 1)) - spread - s.height / 2;
                    if (x < 2 || y < 2 || x + s.width >= w - 2 || y + s.height >= h - 2)
                        continue;
                    bool fits = true;
                    for (int sy = 0; sy < s.height && fits; sy++)
                        for (int sx = 0; sx < s.width; sx++)
                        {
                            if (s.pixels[sy * s.width + sx].a == 0)
                                continue;
                            int px = x + sx, py = y + sy;
                            bool allowed =
                                grassRoute >= 0
                                    ? GrassWalkPixel(l, grassRoute, px, py)
                                    : MaterialAt(l, px, py, true) &&
                                          PastoralGridGenerator.DistanceToRoad(l.map, px, py) <= -5;
                            if (!allowed)
                            {
                                fits = false;
                                break;
                            }
                            for (int dy = -2; dy <= 2 && fits; dy++)
                                for (int dx = -2; dx <= 2; dx++)
                                    if (occupied[(py + dy) * w + px + dx])
                                    {
                                        fits = false;
                                        break;
                                    }
                        }
                    if (!fits)
                        continue;
                    for (int sy = 0; sy < s.height; sy++)
                        for (int sx = 0; sx < s.width; sx++)
                        {
                            var p = s.pixels[sy * s.width + sx];
                            if (p.a == 0)
                                continue;
                            int i = (y + sy) * w + x + sx;
                            target[i] = ForestRaster.Over(target[i], p);
                            occupied[i] = true;
                        }
                    break;
                }
            }
        }
    }
}
