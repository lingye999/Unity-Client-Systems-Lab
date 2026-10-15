using System;
using System.Collections.Generic;
namespace ForgottenMemories.PastoralGridV2
{
    // 生成算法只处理数据。Perlin 提供环境倾向，硬约束决定落点与可通行关系。
    public static class PastoralGridGenerator
    {
        const int W = ForestMapMetrics.Width, H = ForestMapMetrics.Height, T = ForestMapMetrics.TilePixels,
                  PW = W * T, PH = H * T;
        const int STEP = ForestMapMetrics.NavigationStep, NW = PW / STEP, NH = PH / STEP;
        const double PLAYER = ForestMapMetrics.PlayerRadiusPixels;
        static readonly int[] DX = { 1, 0, -1, 0 }, DY = { 0, 1, 0, -1 };

        sealed class Rng
        {
            uint state;

            public Rng(int seed, uint stage)
            {
                state = Hash(unchecked((uint)seed) ^ stage);
                if (state == 0)
                    state = 1;
            }

            public uint Next()
            {
                unchecked
                {
                    state ^= state << 13;
                    state ^= state >> 17;
                    state ^= state << 5;
                }
                return state;
            }

            public int Int(int a, int b)
            {
                return a + (int)(Next() % (uint)(b - a));
            }

            public double Unit()
            {
                return (Next() >> 8) / 16777216.0;
            }
        }

        static uint Hash(uint x)
        {
            return SeededFields.Hash(x);
        }

        static double Clamp(double x, double a, double b)
        {
            return Math.Max(a, Math.Min(b, x));
        }

        static double Length(double x, double y)
        {
            return Math.Sqrt(x * x + y * y);
        }

        static Point Snap(int x, int y)
        {
            return new Point((int)Clamp(x / STEP, 3, NW - 4) * STEP + 8,
                             (int)Clamp(y / STEP, 3, NH - 4) * STEP + 8);
        }

        static FastNoiseLite Field(int seed, uint salt)
        {
            return SeededFields.Perlin(seed, salt);
        }

        public static PastoralLayout Generate(int seed, int style, Catalog catalog, float vegetation = 1f,
                                              bool withRiver = false)
        {
            return MapGenerationPipeline
                .BuildLayout(new MapGenerationRequest(seed, style, vegetation, withRiver), catalog)
                .Layout;
        }

        internal static PastoralLayout CreateLayout(MapGenerationRequest request)
        {
            int seed = request.Seed, style = request.Style;
            bool withRiver = request.WithRiver;
            var m =
                new Map { algorithmVersion = withRiver ? "pastoral-dualgrid-2.2" : "pastoral-dualgrid-2.1",
                          seed = seed,
                          profileIndex = style,
                          profile = new[] { "village_meadow", "grove_commons", "woodland_lanes",
                                            "scenic_woodland" }[style],
                          vegetation = request.Vegetation,
                          shoreMasks = new int[W * H],
                          terrainSurface = new int[W * H],
                          terrainBlocked = new int[W * H],
                          groundVariant = new int[W * H],
                          density = new int[W * H],
                          habitat = new int[W * H],
                          waterVariant = new int[W * H],
                          waterElevation = new double[W * H],
                          riverLeft = new int[H + 1],
                          riverRight = new int[H + 1],
                          riverCenterPixels = new int[H + 1],
                          bankLeftPixels = new int[PH + 1],
                          bankRightPixels = new int[PH + 1],
                          logicalLand = new int[(NW + 1) * (NH + 1)],
                          displayShoreMasks = new int[NW * NH],
                          displayBlocked = new int[NW * NH],
                          highlands = new Highland[0],
                          terrainSprites = new Stamp[0] };
            for (int i = 0; i < W * H; i++)
            {
                m.shoreMasks[i] = 15;
                m.waterElevation[i] = -1;
                m.groundVariant[i] = (int)(Hash(unchecked((uint)seed ^ (uint)i * 997U)) % 160);
            }
            for (int i = 0; i < m.logicalLand.Length; i++)
                m.logicalLand[i] = 1;
            for (int i = 0; i < m.displayShoreMasks.Length; i++)
                m.displayShoreMasks[i] = 15;
            for (int i = 0; i <= PH; i++)
            {
                m.bankLeftPixels[i] = -10000;
                m.bankRightPixels[i] = -10000;
            }
            var l = new PastoralLayout { map = m,
                                         scenicPaths = style == 3,
                                         soilSamples = new int[(W + 1) * (H + 1)],
                                         soilMasks = new int[W * H],
                                         regions = new int[W * H],
                                         roadClass = new int[W * H],
                                         maintenance = new int[W * H] };
            return l;
        }

        sealed class Heap
        {
            readonly List<int> ids = new List<int>();
            readonly List<double> scores = new List<double>();
            public int Count
            {
                get {
                    return ids.Count;
                }
            }

            public void Push(int id, double score)
            {
                int i = ids.Count;
                ids.Add(id);
                scores.Add(score);
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (scores[p] <= score)
                        break;
                    ids[i] = ids[p];
                    scores[i] = scores[p];
                    i = p;
                }
                ids[i] = id;
                scores[i] = score;
            }

            public int Pop()
            {
                int answer = ids[0], last = ids.Count - 1, id = ids[last];
                double score = scores[last];
                ids.RemoveAt(last);
                scores.RemoveAt(last);
                if (last == 0)
                    return answer;
                int i = 0;
                while (i * 2 + 1 < last)
                {
                    int c = i * 2 + 1;
                    if (c + 1 < last && scores[c + 1] < scores[c])
                        c++;
                    if (scores[c] >= score)
                        break;
                    ids[i] = ids[c];
                    scores[i] = scores[c];
                    i = c;
                }
                ids[i] = id;
                scores[i] = score;
                return answer;
            }
        }

        static Point[] AStar(PastoralLayout l, Point start, Point goal, List<Route> existing, bool avoidRoads,
                             FastNoiseLite macro)
        {
            var m = l.map;
            int sx = start.x / STEP, sy = start.y / STEP, gx = goal.x / STEP, gy = goal.y / STEP,
                count = NW * NH * 4;
            var costs = new double[count];
            var parents = new int[count];
            var closed = new bool[count];
            for (int i = 0; i < count; i++)
            {
                costs[i] = double.MaxValue;
                parents[i] = -1;
            }
            var heap = new Heap();
            for (int d = 0; d < 4; d++)
            {
                int key = (sy * NW + sx) * 4 + d;
                costs[key] = 0;
                heap.Push(key, Math.Abs(gx - sx) + Math.Abs(gy - sy));
            }
            // 搜索前缓存代价场，避免 A* 每扩展一个节点都重新扫描已有道路。
            var field = new double[NW * NH];
            for (int y = 3; y < NH - 3; y++)
                for (int x = 3; x < NW - 3; x++)
                {
                    double wx = x * STEP + 8, wy = y * STEP + 8;
                    // 寻路时就预留完整路宽、双网格路肩和岸边余量，不能最后用水面盖路。
                    if (l.river != null && PastoralRiverGenerator.DistanceToWater(l, wx, wy) < 64)
                    {
                        field[y * NW + x] = double.PositiveInfinity;
                        continue;
                    }
                    double c = 1.0 + .48 * (macro.GetNoise((float)(wx / 420), (float)(wy / 420)) + .7);
                    if (existing.Count > 0)
                    {
                        double rd = RoadDistance(existing, wx, wy),
                               ends = Math.Min(Length(wx - start.x, wy - start.y),
                                               Length(wx - goal.x, wy - goal.y));
                        // A loop must go around the spine, rather than repeatedly
                        // crossing it and creating narrow slivers. Endpoint discs
                        // permit a clean approach to the two intended junctions.
                        if (avoidRoads && ends > 110 && rd < 44)
                        {
                            field[y * NW + x] = double.PositiveInfinity;
                            continue;
                        }
                        if (avoidRoads && ends > 75)
                            c += 5 * Clamp((85 - rd) / 85, 0, 1);
                        else if (!avoidRoads && rd < 3)
                            c -= .12;
                    }
                    field[y * NW + x] = Math.Max(.9, c);
                }
            int found = -1;
            while (heap.Count > 0)
            {
                int key = heap.Pop();
                if (closed[key])
                    continue;
                closed[key] = true;
                int cell = key / 4, x = cell % NW, y = cell / NW, oldDirection = key % 4;
                if (x == gx && y == gy)
                {
                    found = key;
                    break;
                }
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + DX[d], ny = y + DY[d];
                    if (nx < 3 || nx >= NW - 3 || ny < 3 || ny >= NH - 3)
                        continue;
                    int next = (ny * NW + nx) * 4 + d;
                    double turn = d == oldDirection ? 0 : d == (oldDirection + 2) % 4 ? 12 : 3.3;
                    double value = costs[key] + field[ny * NW + nx] + turn;
                    if (value >= costs[next])
                        continue;
                    costs[next] = value;
                    parents[next] = key;
                    heap.Push(next, value + .9 * (Math.Abs(gx - nx) + Math.Abs(gy - ny)));
                }
            }
            if (found < 0)
                throw new InvalidOperationException("A* could not connect mandatory places.");
            var points = new List<Point>();
            for (int k = found; k >= 0; k = parents[k])
            {
                int cell = k / 4;
                points.Add(new Point(cell % NW * STEP + 8, cell / NW * STEP + 8));
            }
            points.Reverse();
            return points.ToArray();
        }

        static Route Road(string kind, int radius, params Point[][] pieces)
        {
            var p = new List<Point>();
            foreach (var a in pieces)
                foreach (var q in a)
                    if (p.Count == 0 || p[p.Count - 1].x != q.x || p[p.Count - 1].y != q.y)
                        p.Add(q);
            var r = new int[p.Count];
            for (int i = 0; i < r.Length; i++)
                r[i] = radius;
            return new Route { kind = kind, points = p.ToArray(), radii = r };
        }

        static Point Nearest(List<Route> routes, Point goal)
        {
            Point best = routes[0].points[0];
            double distance = double.MaxValue;
            foreach (var r in routes)
                foreach (var p in r.points)
                {
                    double d = Length(p.x - goal.x, p.y - goal.y);
                    if (d < distance)
                    {
                        distance = d;
                        best = p;
                    }
                }
            return best;
        }

        static Clearing ClearingAt(Point p, int rx, int ry, double phase)
        {
            var c = new Clearing { x = p.x, y = p.y, rx = rx, ry = ry, outline = new Point[40] };
            for (int i = 0; i < c.outline.Length; i++)
            {
                double a = i * Math.PI * 2 / c.outline.Length,
                       r = .95 + .025 * Math.Sin(3 * a + phase) + .02 * Math.Cos(5 * a - phase);
                c.outline[i] =
                    new Point(c.x + (int)(rx * r * Math.Cos(a)), c.y + (int)(ry * r * Math.Sin(a)));
            }
            return c;
        }

        internal static void ComposeRoads(PastoralLayout l)
        {
            var m = l.map;
            var rng = new Rng(unchecked(m.seed ^ m.profileIndex * 7139), 0x726f6164U);
            var macro = Field(unchecked(m.seed ^ m.profileIndex * 19687), 0x6d616372U);
            var routes = new List<Route>();
            var infos = new List<RoadInfo>();
            var clearings = new List<Clearing>();
            Point start = Snap(72, rng.Int(l.river == null ? 310 : 570, 850)),
                  end = Snap(PW - 72, rng.Int(l.river == null ? 310 : 570, 850));
            Point hub =
                Snap(rng.Int(625, 920), rng.Int(l.river == null ? 375 : 590, l.river == null ? 790 : 840));
            var p1 = AStar(l, start, hub, routes, false, macro);
            var p2 = AStar(l, hub, end, routes, false, macro);
            int mainRadius = m.profileIndex == 2 ? 23 : 26;
            routes.Add(Road("main", mainRadius, p1, p2));
            infos.Add(new RoadInfo { routeIndex = 0, fromNode = 0, toNode = 1, halfWidthPixels = mainRadius,
                                     hierarchy = "main" });
            m.entrance = start;
            m.exit = end;
            // One useful detour connects separated positions on the spine. Its
            // open centre remains a meadow/grove parcel rather than a tiny loop.
            var main = routes[0];
            Point joinA = main.points[main.points.Length * 29 / 100],
                  joinB = main.points[main.points.Length * 72 / 100];
            int segmentMinY = PH, segmentMaxY = 0;
            for (int i = main.points.Length * 29 / 100; i <= main.points.Length * 72 / 100; i++)
            {
                segmentMinY = Math.Min(segmentMinY, main.points[i].y);
                segmentMaxY = Math.Max(segmentMaxY, main.points[i].y);
            }
            int upper = segmentMinY - 115, lower = PH - 115 - segmentMaxY;
            int minimumLoopY = 150;
            if (l.river != null)
            {
                int bottom = 0;
                foreach (int y in l.river.bankBottom)
                    bottom = Math.Max(bottom, y);
                minimumLoopY = bottom + 82;
                upper = segmentMinY - minimumLoopY;
            }
            int sign = upper > lower ? -1 : 1;
            if (Math.Abs(upper - lower) < 120 && rng.Unit() < .5)
                sign = -sign;
            int detour = rng.Int(195, 278), targetY = sign < 0 ? segmentMinY - detour : segmentMaxY + detour;
            targetY = (int)Clamp(targetY, minimumLoopY, PH - 150);
            Point guideA = Snap(joinA.x, targetY), guideB = Snap(joinB.x, targetY);
            var la = AStar(l, joinA, guideA, routes, true, macro);
            var lb = AStar(l, guideA, guideB, routes, true, macro);
            var lc = AStar(l, guideB, joinB, routes, true, macro);
            var loop = Road("loop", 20, la, lb, lc);
            routes.Add(loop);
            infos.Add(new RoadInfo { routeIndex = 1, fromNode = 2, toNode = 3, halfWidthPixels = 20,
                                     hierarchy = "loop", closesLoop = true });
            foreach (var p in loop.points)
                if (RoadDistance(new List<Route> { main }, p.x, p.y) > 65)
                    l.loopDetourPixels += STEP;
            // A main activity room is grass, with its own approach. Further
            // clearings terminate branches, so every spur has a readable purpose.
            int branchCount = 2 + (int)(Hash(unchecked((uint)m.seed) ^ 0x6272616eU) % 2);
            for (int b = 0; b < branchCount; b++)
            {
                Point target = new Point();
                double best = double.MinValue;
                for (int k = 0; k < 420; k++)
                {
                    Point candidate = Snap(rng.Int(180, PW - 180), rng.Int(180, PH - 150));
                    double rd = RoadDistance(routes, candidate.x, candidate.y);
                    // The opposite bank is landscape only. A complete activity
                    // room and approach must fit on the entrance's dry bank.
                    if (l.river != null &&
                        (candidate.y < l.river.bankBottom[candidate.x] ||
                         !PastoralRiverGenerator.DryDisk(l, candidate.x, candidate.y, 120)))
                        continue;
                    if (rd < 120 || rd > 310)
                        continue;
                    bool close = false;
                    foreach (var c in clearings)
                        if (Length(candidate.x - c.x, candidate.y - c.y) < 230)
                            close = true;
                    if (close)
                        continue;
                    double n = macro.GetNoise((float)(candidate.x / 390.0), (float)(candidate.y / 390.0));
                    double score = rd * .3 - n * 48 + rng.Unit() * 22;
                    if (score > best)
                    {
                        best = score;
                        target = candidate;
                    }
                }
                if (best == double.MinValue)
                    continue;
                Point join = Nearest(routes, target);
                var path = AStar(l, join, target, routes, false, macro);
                int branchRadius = l.scenicPaths ? 20 : 16;
                routes.Add(Road("branch", branchRadius, path));
                infos.Add(new RoadInfo { routeIndex = routes.Count - 1, fromNode = 4 + b, toNode = 10 + b,
                                         halfWidthPixels = branchRadius, hierarchy = "branch" });
                clearings.Add(
                    ClearingAt(target, b == 0 ? 108 : 82, b == 0 ? 82 : 65, rng.Unit() * Math.PI * 2));
            }
            // The hub is an inhabited crossroads, not another huge soil disk.
            m.routes = routes.ToArray();
            m.clearings = clearings.ToArray();
            l.roadInfo = infos.ToArray();
            l.routeSurface = new int[m.routes.Length];
            if (l.scenicPaths)
                for (int i = 0; i < l.routeSurface.Length; i++)
                    l.routeSurface[i] = 1;
            BuildRoadGraph(l);
            var goals = new List<Point>();
            foreach (var c in clearings)
                goals.Add(Snap(c.x, c.y));
            m.collectibles = goals.ToArray();
            m.ports = new[] { new Port { kind = "road_candidate", edge = "W", direction = "in",
                                         offset = start.y, width = 52, level = 0 },
                              new Port { kind = "road_candidate", edge = "E", direction = "out",
                                         offset = end.y, width = 52, level = 0 } };
            var pavers = new List<Paver>();
            // Small native paver groups are clustered around junctions. The
            // renderer uses the separate native gravel layer along main roads.
            var paveRng = new Rng(m.seed, 0x70617665U);
            foreach (var r in routes)
            {
                if (r.kind == "loop")
                    continue;
                for (int i = 2; i < r.points.Length - 2; i++)
                {
                    var p = r.points[i];
                    double junction = Math.Min(
                        Length(p.x - hub.x, p.y - hub.y),
                        Math.Min(Length(p.x - joinA.x, p.y - joinA.y), Length(p.x - joinB.x, p.y - joinB.y)));
                    if (junction > 85 || i % 2 != 0 || paveRng.Unit() > .30)
                        continue;
                    pavers.Add(new Paver { x = p.x - 16 + paveRng.Int(-3, 4),
                                           y = p.y - 16 + paveRng.Int(-3, 4), variant = paveRng.Int(0, 16) });
                }
            }
            // Scenic paths use whole small native stones in the independent
            // renderer, rather than forcing a continuous square paving stripe.
            m.pavers = l.scenicPaths ? new Paver[0] : pavers.ToArray();
        }

        static void BuildRoadGraph(PastoralLayout l)
        {
            // Build topology from distinct axial centreline edges, then compress
            // degree-two runs. Thick road/nav raster cycles are irrelevant here.
            var adjacency = new Dictionary<int, List<int>>();
            var hierarchy = new Dictionary<long, int>();
            for (int r = 0; r < l.map.routes.Length; r++)
            {
                var points = l.map.routes[r].points;
                for (int i = 1; i < points.Length; i++)
                {
                    int a = points[i - 1].y / STEP * NW + points[i - 1].x / STEP,
                        b = points[i].y / STEP * NW + points[i].x / STEP;
                    if (a == b)
                        continue;
                    List<int> ns;
                    if (!adjacency.TryGetValue(a, out ns))
                    {
                        ns = new List<int>();
                        adjacency.Add(a, ns);
                    }
                    if (!ns.Contains(b))
                        ns.Add(b);
                    if (!adjacency.TryGetValue(b, out ns))
                    {
                        ns = new List<int>();
                        adjacency.Add(b, ns);
                    }
                    if (!ns.Contains(a))
                        ns.Add(a);
                    long key = ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
                    if (!hierarchy.ContainsKey(key))
                        hierarchy.Add(key, r);
                }
            }
            var nodes = new List<RoadNode>();
            var nodeId = new Dictionary<int, int>();
            int entrance = l.map.entrance.y / STEP * NW + l.map.entrance.x / STEP,
                exit = l.map.exit.y / STEP * NW + l.map.exit.x / STEP;
            foreach (var pair in adjacency)
                if (pair.Value.Count != 2 || pair.Key == entrance || pair.Key == exit)
                {
                    int id = nodes.Count;
                    nodeId.Add(pair.Key, id);
                    nodes.Add(new RoadNode { id = id, x = pair.Key % NW * STEP + 8,
                                             y = pair.Key / NW * STEP + 8,
                                             kind = pair.Key == entrance    ? "entrance"
                                                    : pair.Key == exit      ? "exit"
                                                    : pair.Value.Count == 1 ? "destination"
                                                                            : "junction" });
                }
            var edges = new List<RoadEdge>();
            var used = new HashSet<long>();
            foreach (var pair in nodeId)
                foreach (int adjacent in adjacency[pair.Key])
                {
                    long first =
                        ((long)Math.Min(pair.Key, adjacent) << 32) | (uint)Math.Max(pair.Key, adjacent);
                    if (used.Contains(first))
                        continue;
                    used.Add(first);
                    int previous = pair.Key, current = adjacent, length = STEP, route = hierarchy[first];
                    while (!nodeId.ContainsKey(current))
                    {
                        var ns = adjacency[current];
                        int next = ns[0] == previous ? ns[1] : ns[0];
                        long key = ((long)Math.Min(current, next) << 32) | (uint)Math.Max(current, next);
                        used.Add(key);
                        previous = current;
                        current = next;
                        length += STEP;
                    }
                    edges.Add(new RoadEdge { from = pair.Value, to = nodeId[current], routeIndex = route,
                                             lengthPixels = length, hierarchy = l.map.routes[route].kind });
                }
            l.roadNodes = nodes.ToArray();
            l.roadEdges = edges.ToArray();
            l.roadGraphCycles = edges.Count - nodes.Count + 1;
        }

        static double SegmentDistance(double x, double y, Point a, Point b)
        {
            double dx = b.x - a.x, dy = b.y - a.y,
                   t = Clamp(((x - a.x) * dx + (y - a.y) * dy) / Math.Max(1, dx * dx + dy * dy), 0, 1);
            return Length(x - a.x - t * dx, y - a.y - t * dy);
        }

        static double RoadDistance(List<Route> routes, double x, double y)
        {
            double best = double.MaxValue;
            foreach (var r in routes)
                for (int i = 1; i < r.points.Length; i++)
                    best = Math.Min(best, SegmentDistance(x, y, r.points[i - 1], r.points[i]) -
                                              Math.Max(r.radii[i - 1], r.radii[i]));
            return best;
        }

        static double RouteDistance(Route r, double x, double y)
        {
            double best = double.MaxValue;
            for (int i = 1; i < r.points.Length; i++)
                best = Math.Min(best, SegmentDistance(x, y, r.points[i - 1], r.points[i]) -
                                          Math.Max(r.radii[i - 1], r.radii[i]));
            return best;
        }

        internal static void PrepareRoadDistance(Map m)
        {
            var routes = new List<Route>(m.routes);
            m.routeClearance = new double[PW / 8 * (PH / 8)];
            for (int y = 0; y < PH / 8; y++)
                for (int x = 0; x < PW / 8; x++)
                    m.routeClearance[y * (PW / 8) + x] = RoadDistance(routes, x * 8 + 4, y * 8 + 4) - 6;
        }

        public static double DistanceToRoad(Map m, double x, double y)
        {
            return MapGeometry.DistanceToRoad(m, x, y);
        }

        public static bool IsClearing(Map m, double x, double y, double margin = 0)
        {
            return MapGeometry.IsClearing(m, x, y, margin);
        }

        public static bool IsGrassRoute(PastoralLayout l, int routeIndex)
        {
            return l.routeSurface != null && routeIndex >= 0 && routeIndex < l.routeSurface.Length &&
                   l.routeSurface[routeIndex] == 1;
        }

        public static int SoilMask(PastoralLayout l, int x, int y)
        {
            int i = y * (W + 1) + x;
            return l.soilSamples[i] | l.soilSamples[i + 1] << 1 | l.soilSamples[i + W + 2] << 2 |
                   l.soilSamples[i + W + 1] << 3;
        }

        internal static void BuildDualGrid(PastoralLayout l)
        {
            var m = l.map;
            var routes = new List<Route>();
            for (int i = 0; i < m.routes.Length; i++)
                if (!IsGrassRoute(l, i))
                    routes.Add(m.routes[i]);
            for (int y = 0; y <= H; y++)
                for (int x = 0; x <= W; x++)
                    l.soilSamples[y * (W + 1) + x] = RoadDistance(routes, x * T, y * T) <= 0 ? 1 : 0;
            // Native atlas has fourteen unambiguous corner masks. Resolve only
            // diagonal checkerboards by adding soil, never erasing a thin path.
            // Monotonic updates terminate; each correction changes at most one
            // display cell's two remaining corner samples.
            bool changed = true;
            int rounds = 0;
            while (changed && rounds++ < W * H)
            {
                changed = false;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int mask = SoilMask(l, x, y);
                        if (mask != 5 && mask != 10)
                            continue;
                        int i = y * (W + 1) + x;
                        l.soilSamples[i] = l.soilSamples[i + 1] = l.soilSamples[i + W + 1] =
                            l.soilSamples[i + W + 2] = 1;
                        l.ambiguityCorrections++;
                        changed = true;
                    }
            }
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    l.soilMasks[i] = SoilMask(l, x, y);
                    double best = double.MaxValue;
                    int kind = 0;
                    for (int r = 0; r < m.routes.Length; r++)
                    {
                        double d = RouteDistance(m.routes[r], x * T + 16, y * T + 16);
                        if (d < best)
                        {
                            best = d;
                            kind = m.routes[r].kind == "main" ? 1 : m.routes[r].kind == "branch" ? 2 : 3;
                        }
                    }
                    l.roadClass[i] = best < 14 ? kind : 0;
                }
        }

        public static double MaintenanceAt(Map m, double x, double y)
        {
            double value = 0;
            foreach (var r in m.routes)
            {
                double influence = r.kind == "main" ? 1 : r.kind == "loop" ? .82 : .70;
                double reach = (r.kind == "main"   ? 145
                                : r.kind == "loop" ? 110
                                                   : 92) *
                               (m.profileIndex == 0   ? 1.08
                                : m.profileIndex == 2 ? .90
                                                      : 1);
                value =
                    Math.Max(value, influence * Clamp(1 - Math.Max(0, RouteDistance(r, x, y)) / reach, 0, 1));
            }
            foreach (var c in m.clearings)
            {
                double r = Length((x - c.x) / Math.Max(1, c.rx), (y - c.y) / Math.Max(1, c.ry));
                value = Math.Max(value, .88 * Clamp((1.65 - r) / .65, 0, 1));
            }
            return value;
        }

        internal static void BuildRegions(PastoralLayout l)
        {
            var m = l.map;
            var blocked = new bool[W * H];
            var ids = l.regions;
            for (int i = 0; i < ids.Length; i++)
                ids[i] = -1;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    blocked[i] = l.roadClass[i] > 0 || (l.river != null && l.river.waterMasks[i] != 0);
                    l.maintenance[i] = (int)Math.Round(100 * MaintenanceAt(m, x * T + 16, y * T + 16));
                    if ((l.river == null || l.river.waterMasks[i] == 0) &&
                        DistanceToRoad(m, x * T + 16, y * T + 16) > 155 &&
                        !IsClearing(m, x * T + 16, y * T + 16, 25))
                        l.wildInteriorTiles++;
                }
            // Partition-only continuations of the entry/exit meet the map frame;
            // these do not create extra painted roads or physics obstacles.
            for (int x = 0; x <= m.entrance.x / T; x++)
                blocked[m.entrance.y / T * W + x] = true;
            for (int x = m.exit.x / T; x < W; x++)
                blocked[m.exit.y / T * W + x] = true;
            var regionCells = new List<List<int>>();
            var queue = new Queue<int>();
            for (int first = 0; first < W * H; first++)
            {
                if (blocked[first] || ids[first] >= 0)
                    continue;
                int id = regionCells.Count;
                var cells = new List<int>();
                ids[first] = id;
                queue.Enqueue(first);
                while (queue.Count > 0)
                {
                    int i = queue.Dequeue();
                    cells.Add(i);
                    for (int d = 0; d < 4; d++)
                    {
                        int x = i % W + DX[d], y = i / W + DY[d];
                        if (x < 0 || y < 0 || x >= W || y >= H)
                            continue;
                        int j = y * W + x;
                        if (!blocked[j] && ids[j] < 0)
                        {
                            ids[j] = id;
                            queue.Enqueue(j);
                        }
                    }
                }
                regionCells.Add(cells);
            }
            // Tiny fragments have no independent ecology. Merge into the nearest
            // substantial parcel, through the separating road if necessary.
            for (int r = 0; r < regionCells.Count; r++)
                if (regionCells[r].Count < 18)
                {
                    int best = -1;
                    double dist = double.MaxValue;
                    int a = regionCells[r][0];
                    for (int s = 0; s < regionCells.Count; s++)
                        if (regionCells[s].Count >= 18)
                        {
                            int b = regionCells[s][0];
                            double dd = Length(a % W - b % W, a / W - b / W);
                            if (dd < dist)
                            {
                                dist = dd;
                                best = s;
                            }
                        }
                    if (best >= 0)
                    {
                        foreach (int i in regionCells[r])
                        {
                            ids[i] = best;
                            regionCells[best].Add(i);
                        }
                        regionCells[r].Clear();
                    }
                }
            var remap = new Dictionary<int, int>();
            var regions = new List<RegionInfo>();
            for (int r = 0; r < regionCells.Count; r++)
                if (regionCells[r].Count > 0)
                {
                    int id = regions.Count;
                    remap.Add(r, id);
                    double sum = 0;
                    int sx = 0, sy = 0;
                    foreach (int i in regionCells[r])
                    {
                        sum += l.maintenance[i] / 100.0;
                        sx += i % W;
                        sy += i / W;
                    }
                    double average = sum / regionCells[r].Count;
                    regions.Add(new RegionInfo { id = id, tileCount = regionCells[r].Count,
                                                 centerX = sx * T / regionCells[r].Count + 16,
                                                 centerY = sy * T / regionCells[r].Count + 16,
                                                 perlinSeed = unchecked(
                                                     (int)Hash((uint)m.seed ^ (uint)(id + 1) * 0x9e3779b9U)),
                                                 meanMaintenance = average,
                                                 kind = average > .35   ? "meadow"
                                                        : average > .17 ? "sparse_woodland"
                                                                        : "forest_interior",
                                                 vegetationTarget = average > .35   ? .25
                                                                    : average > .17 ? .43
                                                                                    : .67 });
                }
            for (int i = 0; i < ids.Length; i++)
                if (ids[i] >= 0)
                    ids[i] = remap[ids[i]];
            // Assign road samples to the closest land parcel for local field
            // lookup. Actual roads retain their own hierarchy and maintenance.
            for (int i = 0; i < ids.Length; i++)
                if (ids[i] < 0)
                {
                    int answer = 0;
                    double best = double.MaxValue;
                    foreach (var r in regions)
                    {
                        double d = Length(i % W * T + 16 - r.centerX, i / W * T + 16 - r.centerY);
                        if (d < best)
                        {
                            best = d;
                            answer = r.id;
                        }
                    }
                    ids[i] = answer;
                }
            l.regionInfo = regions.ToArray();
        }

        static Dictionary<string, SpriteSpec> Specs(Catalog catalog)
        {
            var d = new Dictionary<string, SpriteSpec>(StringComparer.Ordinal);
            foreach (var s in catalog.sprites)
                d.Add(s.id, s);
            return d;
        }

        static double RegionBlend(PastoralLayout l, FastNoiseLite[] local, double x, double y,
                                  out double target, double scale)
        {
            // 区域编号位于格心。跨区域约一格范围内混合局部场和目标密度，
            // 使生态逐渐过渡；道路与空地的硬净空仍具有更高优先级。
            double gx = Clamp(x / T - .5, 0, W - 1), gy = Clamp(y / T - .5, 0, H - 1);
            int x0 = (int)Math.Floor(gx), y0 = (int)Math.Floor(gy), x1 = Math.Min(W - 1, x0 + 1),
                y1 = Math.Min(H - 1, y0 + 1);
            double fx = gx - x0, fy = gy - y0;
            int a = l.regions[y0 * W + x0], b = l.regions[y0 * W + x1], c = l.regions[y1 * W + x0],
                d = l.regions[y1 * W + x1];
            double wa = (1 - fx) * (1 - fy), wb = fx * (1 - fy), wc = (1 - fx) * fy, wd = fx * fy;
            float px = (float)(x / scale), py = (float)(y / scale);
            target = l.regionInfo[a].vegetationTarget * wa + l.regionInfo[b].vegetationTarget * wb +
                     l.regionInfo[c].vegetationTarget * wc + l.regionInfo[d].vegetationTarget * wd;
            return local[a].GetNoise(px, py) * wa + local[b].GetNoise(px, py) * wb +
                   local[c].GetNoise(px, py) * wc + local[d].GetNoise(px, py) * wd;
        }

        static double Density(PastoralLayout l, FastNoiseLite macro, FastNoiseLite[] local, double x,
                              double y, VegetationRecipe settings)
        {
            var m = l.map;
            double edge = Math.Min(Math.Min(x, PW - x), Math.Min(y, PH - y));
            double maintain = MaintenanceAt(m, x, y);
            double target,
                global = macro.GetNoise((float)(x / settings.macroScalePixels),
                                        (float)(y / settings.macroScalePixels)),
                detail = RegionBlend(l, local, x, y, out target, settings.localScalePixels);
            double d = settings.baseDensity +
                       settings.edgeWeight *
                           (1 - Clamp((edge - settings.edgeStartPixels) / settings.edgeFadePixels, 0, 1)) +
                       settings.macroWeight * global + settings.localWeight * detail +
                       settings.regionWeight * (target - settings.regionReference);
            d *= 1 - settings.maintenanceSuppression * maintain;
            if (IsClearing(m, x, y, settings.clearingMarginPixels))
                return 0;
            d *= Clamp(DistanceToRoad(m, x, y) / settings.roadFadePixels, 0, 1);
            return Clamp(d * m.vegetation, 0, 1);
        }

        internal static PlacementRuleStatistics[] Plant(PastoralLayout l, Catalog catalog,
                                                        ForestGenerationRecipe recipe,
                                                        IPlacementConstraint[] additionalConstraints)
        {
            var m = l.map;
            var specs = Specs(catalog);
            var props = new List<Prop>();
            var groves = new List<Grove>();
            var rules = new PlacementConstraintSet(new PlacementContext(l, recipe.placement, props, specs),
                                                   additionalConstraints);
            var macro = Field(m.seed, 0x65636f6cU);
            var local = new FastNoiseLite[l.regionInfo.Length];
            for (int i = 0; i < local.Length; i++)
                local[i] = Field(l.regionInfo[i].perlinSeed, 0x6c6f636cU);
            var rng = new Rng(m.seed, 0x74726565U);
            int greenTrees = 0, warmTrees = 0, cyanTrees = 0, shrubs = 0;
            int shrubBudget = (int)Math.Round((l.scenicPaths ? 218 : 180) * m.vegetation);
            Func<string, int, int, int, bool> put = (id, x, y, group) =>
            {
                SpriteSpec s;
                if (!specs.TryGetValue(id, out s) || (s.family == "bush" && shrubs >= shrubBudget) ||
                    !rules.Allows(s, x, y))
                    return false;
                props.Add(new Prop { id = id, instanceId = "pastoral_" + props.Count, rootX = x, rootY = y,
                                     level = 0, group = group });
                if (s.family == "tree")
                {
                    if (id == "tree_fir" || id == "tree_willow")
                        greenTrees++;
                    else if (id == "tree_cyan")
                        cyanTrees++;
                    else
                        warmTrees++;
                }
                if (s.family == "bush")
                    shrubs++;
                return true;
            };
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    m.density[i] = (int)Math.Round(
                        100 * Density(l, macro, local, x * T + 16, y * T + 16, recipe.vegetation));
                    m.habitat[i] = m.density[i] > 38 ? 2 : m.density[i] > 17 ? 1 : 0;
                }
            // 先组织有共同中心和主树种的林团，再用少量伴生植物丰富边缘。
            int wantedGroves = (int)((m.profileIndex == 1 ? 24
                                      : l.scenicPaths     ? 23
                                                          : 21) *
                                     m.vegetation),
                cyanGroves = 0;
            for (int attempt = 0; attempt < 2200 && groves.Count < wantedGroves; attempt++)
            {
                int x = rng.Int(65, PW - 65), y = rng.Int(160, PH - 45);
                double density = Density(l, macro, local, x, y, recipe.vegetation);
                if (density < .20 || rng.Unit() > density)
                    continue;
                bool close = false;
                foreach (var g in groves)
                    if (Length(x - g.x, y - g.y) < 132)
                    {
                        close = true;
                        break;
                    }
                if (close)
                    continue;
                int region = l.regions[y / T * W + x / T];
                // 配色按实际接受的高树统计。桦树原图偏黄，不能只凭素材名归入绿色。
                int treeTotal = greenTrees + warmTrees + cyanTrees;
                bool regionalAccent = (Hash(unchecked((uint)l.regionInfo[region].perlinSeed)) % 7) == 0;
                bool accent = cyanGroves < Math.Max(1, wantedGroves / 10) && treeTotal >= 18 &&
                              cyanTrees < treeTotal * .075 && (regionalAccent || groves.Count > 8);
                string id = accent                                           ? "tree_cyan"
                            : treeTotal == 0 || greenTrees < treeTotal * .57 ? "tree_fir"
                            : rng.Unit() < .85                               ? "tree_gold"
                                                                             : "tree_autumn";
                int groupId = groves.Count;
                var grove = new Grove { x = x, y = y, radius = rng.Int(70, 111), species = id,
                                        wanted = rng.Int(3, 6) };
                for (int k = 0; k < 100 && grove.accepted < grove.wanted; k++)
                {
                    double a = rng.Unit() * Math.PI * 2, r = Math.Sqrt(rng.Unit()) * grove.radius;
                    int px = x + (int)(Math.Cos(a) * r), py = y + (int)(Math.Sin(a) * r * .74);
                    if (put(k % 6 == 4 ? "sapling_green" : id, px, py, groupId))
                        grove.accepted++;
                }
                if (grove.accepted > 1)
                {
                    groves.Add(grove);
                    if (accent)
                        cyanGroves++;
                }
                else if (grove.accepted == 1)
                {
                    props[props.Count - 1].group = -1;
                    if (accent)
                        cyanGroves++;
                }
            }
            for (int k = 0; k < 160; k++)
            {
                int x = rng.Int(135, PW - 135), y = rng.Int(255, PH - 70);
                if (Density(l, macro, local, x, y, recipe.vegetation) > .28 && put("tree_willow", x, y, -1))
                    break;
            }
            int solitary = 0;
            for (int k = 0; k < 400 && solitary < 11; k++)
            {
                int x = rng.Int(50, PW - 50), y = rng.Int(150, PH - 30);
                string id =
                    greenTrees < (greenTrees + warmTrees + cyanTrees) * .57 ? "tree_fir" : "tree_gold";
                if (Density(l, macro, local, x, y, recipe.vegetation) > .15 && put(id, x, y, -1))
                    solitary++;
            }
            var decor = new Rng(m.seed, 0x6465636fU);
            var roots = new List<Prop>(props);
            foreach (var p in roots)
            {
                double maintenance = MaintenanceAt(m, p.rootX, p.rootY);
                if (decor.Unit() > (l.scenicPaths ? .86 : .85) - .35 * maintenance)
                    continue;
                int wanted = decor.Int(1, l.scenicPaths ? 5 : 4), remaining = wanted;
                for (int a = 0; a < wanted * 9 && remaining > 0; a++)
                {
                    double angle = decor.Unit() * Math.PI * 2;
                    int radius = decor.Int(35, 91), x = p.rootX + (int)(radius * Math.Cos(angle)),
                        y = p.rootY + (int)(radius * .65 * Math.Sin(angle));
                    string id = a % 7 == 1 ? "rock_moss_small" : "bush_green_" + decor.Int(0, 4);
                    if (put(id, x, y, p.group))
                        remaining--;
                }
            }
            // Additional colonies occupy grove skirts and the outside of path
            // reserves. Their members share a centre; they are not even scatter.
            int wantedColonies = Math.Max(6, (int)((l.scenicPaths ? 12 : 10) * m.vegetation));
            for (int attempt = 0; attempt < 1000 && l.shrubColonies < wantedColonies && roots.Count > 0;
                 attempt++)
            {
                var anchor = roots[decor.Int(0, roots.Count)];
                double angle = decor.Unit() * Math.PI * 2;
                int cx = anchor.rootX + (int)(decor.Int(65, 125) * Math.Cos(angle)),
                    cy = anchor.rootY + (int)(decor.Int(50, 100) * Math.Sin(angle));
                if (cx < 40 || cx > PW - 40 || cy < 70 || cy > PH - 40 ||
                    Density(l, macro, local, cx, cy, recipe.vegetation) < .08 ||
                    MaintenanceAt(m, cx, cy) > .75)
                    continue;
                int members = 0, wanted = decor.Int(3, 6);
                for (int k = 0; k < 36 && members < wanted; k++)
                {
                    double a = decor.Unit() * Math.PI * 2, r = Math.Sqrt(decor.Unit()) * 53;
                    if (put("bush_green_" + decor.Int(0, 4), cx + (int)(r * Math.Cos(a)),
                            cy + (int)(r * .70 * Math.Sin(a)), anchor.group))
                        members++;
                }
                if (members >= 2)
                    l.shrubColonies++;
            }
            // Wet-bank vegetation forms a handful of small colonies with open
            // shore intervals. Roots remain dry; nothing is scattered in water.
            if (l.river != null)
            {
                for (int colony = 0; colony < 7; colony++)
                {
                    int cx = decor.Int(95, PW - 95), cy = l.river.bankBottom[cx] + decor.Int(35, 70),
                        members = 0;
                    for (int k = 0; k < 30 && members < 4; k++)
                    {
                        int x = cx + decor.Int(-43, 44), y = cy + decor.Int(-18, 27);
                        if (put(k % 6 == 0 ? "rock_moss_small" : "bush_green_" + decor.Int(0, 4), x, y, -1))
                            members++;
                    }
                    if (members >= 2)
                        l.shrubColonies++;
                }
            }
            string[] small = { "sapling_small_green", "rock_moss_small", "pebble_moss_0",
                               "pebble_moss_1",       "stump_fir",       "bush_green_0" };
            int[] budgets = { 15, 12, 10, 10, 4, 16 };
            for (int family = 0; family < small.Length; family++)
            {
                int accepted = 0;
                for (int attempt = 0; attempt < budgets[family] * 24 && accepted < budgets[family]; attempt++)
                {
                    int x = decor.Int(25, PW - 25), y = decor.Int(60, PH - 25);
                    double density = Density(l, macro, local, x, y, recipe.vegetation);
                    if (density < .12 || decor.Unit() > density + .08)
                        continue;
                    if (put(small[family], x, y, -1))
                        accepted++;
                }
            }
            m.props = props.ToArray();
            m.groves = groves.ToArray();
            l.greenTrees = greenTrees;
            l.warmTrees = warmTrees;
            l.cyanTrees = cyanTrees;
            int tall = greenTrees + warmTrees + cyanTrees;
            l.greenTreeFraction = greenTrees / (double)Math.Max(1, tall);
            l.warmTreeFraction = warmTrees / (double)Math.Max(1, tall);
            l.cyanTreeFraction = cyanTrees / (double)Math.Max(1, tall);
            l.shrubCount = shrubs;
            return rules.SnapshotStatistics();
        }

        public static void RebuildNavigation(PastoralLayout l, Catalog catalog, FenceBarrier[] barriers = null)
        {
            ForestNavigation.Rebuild(l.map, catalog, barriers);
            if (l.river == null)
                return;
            var m = l.map;
            for (int y = 0; y < NH; y++)
                for (int x = 0; x < NW; x++)
                {
                    int i = y * NW + x;
                    if (!PastoralRiverGenerator.DryDisk(l, x * STEP + 8, y * STEP + 8, PLAYER))
                    {
                        m.navWalkable[i] = 0;
                        m.navLinks[i] = 0;
                    }
                }
            for (int i = 0; i < m.navWalkable.Length; i++)
                if (m.navWalkable[i] != 0)
                {
                    var a = new Point(i % NW * STEP + 8, i / NW * STEP + 8);
                    for (int d = 0; d < 4; d++)
                        if ((m.navLinks[i] & (1 << d)) != 0)
                        {
                            int x = i % NW + DX[d], y = i / NW + DY[d], j = y * NW + x;
                            var b = new Point(x * STEP + 8, y * STEP + 8);
                            if (m.navWalkable[j] == 0 || !PastoralRiverGenerator.DrySweep(l, a, b, PLAYER))
                                m.navLinks[i] &= ~(1 << d);
                        }
                }
        }

        public static Report Validate(PastoralLayout l, Catalog catalog)
        {
            var m = l.map;
            Report report = ForestNavigation.Validate(m, catalog);
            var errors = new List<string>(report.failures);
            if (m.algorithmVersion != (l.river == null ? "pastoral-dualgrid-2.1" : "pastoral-dualgrid-2.2"))
                errors.Add("Unexpected V2 algorithm version");
            if (l.wildInteriorTiles < 180)
                errors.Add("Insufficient road-free forest interior");
            if (l.regionInfo.Length < 2)
                errors.Add("Roads do not partition meaningful parcels");
            if (m.clearings.Length < 2)
                errors.Add("Insufficient purposeful branch destinations");
            int loops = 0, branches = 0;
            foreach (var r in l.roadInfo)
            {
                if (r.closesLoop)
                    loops++;
                if (r.hierarchy == "branch")
                    branches++;
            }
            if (loops != 1 || l.loopDetourPixels < 160)
                errors.Add("Missing useful road loop");
            if (l.roadGraphCycles < 1 || l.roadGraphCycles > 3)
                errors.Add("Road graph has missing or excessive loops");
            if (branches < 2 || branches > 4)
                errors.Add("Branch count outside composition budget");
            for (int i = 0; i < l.soilMasks.Length; i++)
                if (l.soilMasks[i] == 5 || l.soilMasks[i] == 10)
                    errors.Add("Unresolved dual-grid diagonal");
            if (l.routeSurface == null || l.routeSurface.Length != m.routes.Length)
                errors.Add("Missing route material metadata");
            for (int ri = 0; ri < m.routes.Length; ri++)
            {
                var r = m.routes[ri];
                for (int i = 1; i < r.points.Length; i++)
                    if (r.points[i].x != r.points[i - 1].x && r.points[i].y != r.points[i - 1].y)
                        errors.Add("Road is not orthogonal");
                if (IsGrassRoute(l, ri))
                    continue;
                foreach (var p in r.points)
                {
                    int x = p.x / T, y = p.y / T;
                    bool soil = false;
                    for (int yy = y; yy <= Math.Min(H, y + 1); yy++)
                        for (int xx = x; xx <= Math.Min(W, x + 1); xx++)
                            if (l.soilSamples[yy * (W + 1) + xx] == 1)
                                soil = true;
                    if (!soil)
                        errors.Add("Dual-grid correction broke a route core");
                }
            }
            double near = 0, far = 0;
            int nc = 0, fc = 0;
            for (int i = 0; i < W * H; i++)
            {
                if (l.maintenance[i] >= 60)
                {
                    near += m.density[i];
                    nc++;
                }
                if (l.maintenance[i] <= 10)
                {
                    far += m.density[i];
                    fc++;
                }
            }
            if (nc == 0 || fc == 0 || near / nc >= far / fc)
                errors.Add("Maintenance gradient does not reduce vegetation");
            if (l.river != null)
            {
                var river = l.river;
                var specs = Specs(catalog);
                report.riverWidthPixelsMin = river.minWidthPixels;
                report.riverWidthPixelsMax = river.maxWidthPixels;
                if (river.minWidthPixels < 64 || river.maxWidthPixels - river.minWidthPixels < 32)
                    errors.Add("River lacks a safe continuous variable width");
                foreach (int mask in river.waterMasks)
                    if (mask == 5 || mask == 10)
                        errors.Add("Unresolved river dual-grid diagonal");
                foreach (var route in m.routes)
                    foreach (var p in route.points)
                        if (!PastoralRiverGenerator.DryDisk(l, p.x, p.y, route.kind == "main" ? 48 : 42))
                            errors.Add("Road shoulder intersects water");
                foreach (var p in m.props)
                    if (!PastoralRiverGenerator.DryDisk(l, p.rootX, p.rootY, specs[p.id].radiusPixels + 3))
                        errors.Add("Vegetation root intersects water bank");
                foreach (var c in m.clearings)
                    if (!PastoralRiverGenerator.DryDisk(l, c.x, c.y, Math.Max(c.rx, c.ry) + 8))
                        errors.Add("Activity clearing intersects water bank");
                foreach (var p in m.collectibles)
                    if (p.y <= river.bankBottom[p.x])
                        errors.Add("Collectible placed on inaccessible scenic bank");
                for (int i = 0; i < m.navWalkable.Length; i++)
                    if (m.navWalkable[i] != 0 &&
                        !PastoralRiverGenerator.DryDisk(l, i % NW * STEP + 8, i / NW * STEP + 8, PLAYER))
                        errors.Add("Navigation enters river hazard");
            }
            report.failures = errors.ToArray();
            report.passed = errors.Count == 0;
            return report;
        }
    }
}
