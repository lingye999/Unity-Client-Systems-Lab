using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace ForgottenMemories.PastoralGridV2
{
    public sealed class PastoralGridV2Runtime : MonoBehaviour
    {
        public int seed = 202;
        [Range(0, 3)]
        public int style;
        [Range(.6f, 1.4f)]
        public float vegetation = 1;
        public bool withRiver;
        [Tooltip("可选的森林配方；留空时使用与现有场景一致的默认参数。")]
        public ForestGenerationProfile generationProfile;
        public PlacementRuleStatistics[] PlacementStatistics { get; private set; }
        public Camera mapCamera;
        public bool showControls = true, closeView = true;
        [Range(3, 12)]
        public float playerViewSize = 5.625f;
        [SerializeField, HideInInspector]
        PastoralLayout gridLayout;
        Map layout
        {
            get {
                return gridLayout == null ? null : gridLayout.map;
            }
        }
        public PastoralLayout GridLayout
        {
            get {
                return gridLayout;
            }
        }
        [SerializeField, HideInInspector] GameObject generated;
        [SerializeField, HideInInspector]
        FencePlan fencePlan;
        public Map Layout
        {
            get {
                return layout;
            }
        }
        public FencePlan FencePlan
        {
            get {
                return fencePlan;
            }
        }
        public GameObject Generated
        {
            get {
                return generated;
            }
        }
        public double GenerationMilliseconds { get; private set; }
        public int PathDecorationPixels { get; private set; }
        public Tilemap RiverTilemap { get; private set; }
        public readonly List<PolygonCollider2D> RiverColliders = new List<PolygonCollider2D>();
        PastoralRiverRaster riverRaster;
        public PastoralRiverRaster RiverRaster
        {
            get {
                LoadCatalog();
                if (riverRaster == null)
                    riverRaster = new PastoralRiverRaster(source);
                return riverRaster;
            }
        }
        public List<Object> NewAssets { get; } = new List<Object>();
        public List<Collider2D> Obstacles { get; } = new List<Collider2D>();
        public List<SpriteRenderer> PropRenderers { get; } = new List<SpriteRenderer>();
        public List<SpriteRenderer> FenceRenderers { get; } = new List<SpriteRenderer>();
        Catalog catalog;
        ForestRaster source;
        PastoralGridRaster raster;
        NativeFenceAssets fenceAssets;
        readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        readonly Dictionary<string, Tile> tiles = new Dictionary<string, Tile>();
        GameObject overlay;
        Sprite overlaySprite;
        Texture2D overlayTexture;
        Font uiFont;
        GUISkin skin;
        Vector2 scroll;
        string seedText = "202", message = "";
        int pending, viewportWidth, viewportHeight;
        bool viewportControls;
        public int diagnosticMode;
        const float PanelWidth = 292;
        static readonly string[] Styles = { "草场路网", "林团环线", "林间支路", "林间观景道" };
        public Catalog Catalog
        {
            get {
                LoadCatalog();
                return catalog;
            }
        }

        public void LoadCatalog()
        {
            if (catalog != null && source != null)
                return;
            var json = Resources.Load<TextAsset>("ForestMap/forest_atlas");
            var atlas = Resources.Load<Texture2D>("ForestMap/forest_atlas");
            if (json == null || atlas == null)
                throw new InvalidOperationException("Missing audited native atlas.");
            catalog = JsonUtility.FromJson<Catalog>(json.text);
            source = new ForestRaster(catalog, atlas);
            raster = new PastoralGridRaster(source);
            fenceAssets = new NativeFenceAssets();
        }

        void Start()
        {
            LoadCatalog();
            if (layout == null || generated == null)
                Regenerate();
            else
            {
                seedText = layout.seed.ToString();
                Obstacles.AddRange(generated.GetComponentsInChildren<Collider2D>());
                foreach (var t in generated.GetComponentsInChildren<Tilemap>())
                    if (t.name == "Native animated river - shared four water corners")
                        RiverTilemap = t;
                foreach (var p in generated.GetComponentsInChildren<PolygonCollider2D>())
                    if (p.name.StartsWith("River solid ", StringComparison.Ordinal))
                        RiverColliders.Add(p);
                message = "已加载双网格森林场景";
            }
            FitCamera();
        }

        public static Vector2 World(int x, int y)
        {
            return new Vector2(x / 32f, -y / 32f);
        }

        public static Vector2 World(Point p)
        {
            return World(p.x, p.y);
        }

        GameObject Group(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent == null ? generated.transform : parent, false);
            return go;
        }

        public void Regenerate()
        {
            LoadCatalog();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            // 完整数据与最终导航先通过验证，再替换 Unity 中的可见对象。
            var request = new MapGenerationRequest(seed, style, vegetation, withRiver);
            var result = MapGenerationPipeline.Build(
                request, catalog, generationProfile == null ? null : generationProfile.CreateSnapshot());
            var nextGrid = result.Layout;
            var next = nextGrid.map;
            var plan = result.Fences;
            var old = generated;
            var oldMap = gridLayout;
            var oldPlan = fencePlan;
            var oldStatistics = PlacementStatistics;
            var oldAssets = NewAssets.ToArray();
            var oldOverlay = overlay;
            var oldOverlaySprite = overlaySprite;
            var oldOverlayTexture = overlayTexture;
            var oldObstacles = Obstacles.ToArray();
            var oldProps = PropRenderers.ToArray();
            var oldFences = FenceRenderers.ToArray();
            var oldRiverTiles = RiverTilemap;
            var oldRiverColliders = RiverColliders.ToArray();
            RiverColliders.Clear();
            RiverTilemap = null;
            NewAssets.Clear();
            sprites.Clear();
            tiles.Clear();
            Obstacles.Clear();
            PropRenderers.Clear();
            FenceRenderers.Clear();
            try
            {
                gridLayout = nextGrid;
                fencePlan = plan;
                generated = new GameObject("Pastoral dual grid V2 | " + next.profile + " | seed " + seed);
                generated.transform.SetParent(transform, false);
                generated.SetActive(false);
                var ground = Group("01 Dual grid - native editable grass and soil tiles");
                ground.AddComponent<Grid>();
                var surface =
                    Tilemap(ground.transform, "Grass and dirt - four shared logical corners", -24000);
                var details = Tilemap(ground.transform, "Quiet grass variation", -23900);
                for (int y = 0; y < next.height; y++)
                    for (int x = 0; x < next.width; x++)
                    {
                        int i = y * next.width + x;
                        var p = new Vector3Int(x, -y - 1, 0);
                        surface.SetTile(p, Tile(PastoralGridRaster.GroundTileId(nextGrid.soilMasks[i],
                                                                                next.groundVariant[i])));
                        int v = next.groundVariant[i];
                        if (nextGrid.soilMasks[i] == 0 && v < 20)
                            details.SetTile(p, Tile("grass_detail_" + (v % 16)));
                    }
                AddRasterChunks(raster.Understory(nextGrid), next.width * 32, next.height * 32,
                                Group("02 Connected wild forest floor").transform, -23500);
                DrawRiver(nextGrid);
                var pathPixels = raster.Gravel(nextGrid);
                int pathPixelCount = 0;
                foreach (var pixel in pathPixels)
                    if (pixel.a != 0)
                        pathPixelCount++;
                AddRasterChunks(pathPixels, next.width * 32, next.height * 32,
                                Group("03 Native stone paths - dirt lanes and grass walks").transform,
                                -23000);
                var props = Group("04 Editable trees, shrubs and stones");
                foreach (var p in next.props)
                {
                    var spec = source.specs[p.id];
                    var r = DrawSprite(Source(p.id, true), World(p.rootX, p.rootY), p.rootY * 16 + 1,
                                       props.transform);
                    r.name = p.instanceId + " | " + p.id;
                    r.gameObject.AddComponent<ForestDepth>();
                    PropRenderers.Add(r);
                    if (source.shadows.ContainsKey(p.id))
                        DrawSprite(Source(p.id, true, true), World(p.rootX, p.rootY), -22500, r.transform);
                    if (spec.radiusPixels > 0)
                    {
                        var c = r.gameObject.AddComponent<CircleCollider2D>();
                        c.radius = spec.radiusPixels / 32f;
                        c.offset = Vector2.zero;
                        Obstacles.Add(c);
                    }
                }
                DrawFences(plan);
                var bounds = Group("06 Map boundary physics");
                Box(bounds.transform, "West", new Vector2(-.25f, -18), new Vector2(.5f, 37));
                Box(bounds.transform, "East", new Vector2(48.25f, -18), new Vector2(.5f, 37));
                Box(bounds.transform, "North", new Vector2(24, .25f), new Vector2(48, .5f));
                Box(bounds.transform, "South", new Vector2(24, -36.25f), new Vector2(48, .5f));
                foreach (var t in generated.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = 8;
                if (old != null)
                    old.SetActive(false);
                generated.SetActive(true);
                Physics2D.SyncTransforms();
                overlay = null;
                overlaySprite = null;
                overlayTexture = null;
                SetDiagnostics(diagnosticMode);
                FitCamera();
                seedText = seed.ToString();
                watch.Stop();
                GenerationMilliseconds = watch.Elapsed.TotalMilliseconds;
                PathDecorationPixels = pathPixelCount;
                PlacementStatistics = result.PlacementStatistics;
                message = "生成完成 · " + next.validation.trees + " 棵树 · " + plan.pieces.Length +
                          " 段栏杆 · " + GenerationMilliseconds.ToString("F0") + " ms";
                if (old != null)
                    Release(old);
                ReleaseOwned(oldAssets);
            }
            catch
            {
                if (generated != null)
                {
                    generated.SetActive(false);
                    Release(generated);
                }
                ReleaseOwned(NewAssets.ToArray());
                NewAssets.Clear();
                NewAssets.AddRange(oldAssets);
                generated = old;
                gridLayout = oldMap;
                fencePlan = oldPlan;
                PlacementStatistics = oldStatistics;
                Obstacles.Clear();
                Obstacles.AddRange(oldObstacles);
                PropRenderers.Clear();
                PropRenderers.AddRange(oldProps);
                FenceRenderers.Clear();
                FenceRenderers.AddRange(oldFences);
                overlay = oldOverlay;
                overlaySprite = oldOverlaySprite;
                overlayTexture = oldOverlayTexture;
                RiverTilemap = oldRiverTiles;
                RiverColliders.Clear();
                RiverColliders.AddRange(oldRiverColliders);
                sprites.Clear();
                tiles.Clear();
                if (old != null)
                    old.SetActive(true);
                throw;
            }
        }

        void DrawRiver(PastoralLayout grid)
        {
            if (grid.river == null)
                return;
            var root = Group("02b River - shared native banks and six animated frames");
            root.AddComponent<Grid>();
            RiverTilemap =
                Tilemap(root.transform, "Native animated river - shared four water corners", -23400);
            RiverTilemap.animationFrameRate = 8;
            var assets = new Dictionary<string, PastoralRiverTile>();
            for (int y = 0; y < grid.map.height; y++)
                for (int x = 0; x < grid.map.width; x++)
                {
                    int mask = grid.river.waterMasks[y * grid.map.width + x];
                    if (mask == 0)
                        continue;
                    int variant = RiverRaster.WaterVariant(seed, x, y),
                        phase = RiverRaster.WaterPhase(seed, x, y);
                    string key = mask + "_" + variant + "_" + phase;
                    PastoralRiverTile tile;
                    if (!assets.TryGetValue(key, out tile))
                    {
                        tile = ScriptableObject.CreateInstance<PastoralRiverTile>();
                        tile.name = "Native river " + key;
                        tile.waterMask = mask;
                        tile.variant = variant;
                        tile.phase = phase;
                        tile.frames = new Sprite[PastoralRiverRaster.FrameCount];
                        for (int f = 0; f < tile.frames.Length; f++)
                            tile.frames[f] = RasterSprite(RiverRaster.TilePixels(mask, f, x, y, seed), 32, 32,
                                                          tile.name + " frame " + f);
                        NewAssets.Add(tile);
                        assets.Add(key, tile);
                    }
                    RiverTilemap.SetTile(new Vector3Int(x, -y - 1, 0), tile);
                    var polygon = PastoralRiverGenerator.WaterPolygon(grid, x, y);
                    var points = new Vector2[polygon.Length];
                    for (int p = 0; p < points.Length; p++)
                        points[p] = World(polygon[p]);
                    var go = Group("River solid " + x + "," + y, root.transform);
                    var collider = go.AddComponent<PolygonCollider2D>();
                    collider.points = points;
                    RiverColliders.Add(collider);
                    Obstacles.Add(collider);
                }
        }

        void DrawFences(FencePlan plan)
        {
            var root = Group("05 Editable native fences - entrances stay open");
            var fenceSprites = new Dictionary<string, Sprite>();
            foreach (var p in plan.pieces)
            {
                if (!fenceSprites.TryGetValue(p.id, out var sprite))
                {
                    sprite = fenceAssets.Sprite(p.id, NewAssets);
                    fenceSprites.Add(p.id, sprite);
                }
                var r = DrawSprite(sprite, World(p.rootX, p.rootY), p.rootY * 16 + 1, root.transform);
                r.name = "Fence group " + p.group + " | " + p.id;
                r.gameObject.AddComponent<ForestDepth>();
                FenceRenderers.Add(r);
            }
            int i = 0;
            foreach (var b in plan.barriers)
            {
                var go = Group("Fence collision " + i++, root.transform);
                var a = World(b.a);
                var z = World(b.b);
                go.transform.position = (a + z) * .5f;
                var delta = z - a;
                go.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                var c = go.AddComponent<CapsuleCollider2D>();
                c.direction = CapsuleDirection2D.Horizontal;
                c.size = new Vector2(delta.magnitude + (float)b.radius / 16f, (float)b.radius / 16f);
                c.offset = Vector2.zero;
                Obstacles.Add(c);
            }
        }

        public static void Release(Object o)
        {
            if (o == null)
                return;
            if (Application.isPlaying)
                Destroy(o);
            else
                DestroyImmediate(o);
        }

        static void ReleaseOwned(IEnumerable<Object> assets)
        {
            foreach (var o in assets)
            {
                if (o == null)
                    continue;
#if UNITY_EDITOR
                if (UnityEditor.EditorUtility.IsPersistent(o))
                    continue;
#endif
                Release(o);
            }
        }

        public void ForgetBakedAssetOwnership()
        {
            NewAssets.Clear();
        }

        void OnDestroy()
        {
            ReleaseOwned(NewAssets);
            if (uiFont != null)
                Release(uiFont);
            if (skin != null)
                Release(skin);
        }

        Sprite Source(string id, bool anchor = false, bool shadow = false)
        {
            string key = id + (anchor ? "#root" : "") + (shadow ? "#shadow" : "");
            if (sprites.TryGetValue(key, out var s))
                return s;
            var spec = source.specs[id];
            var pivot =
                anchor ? new Vector2(spec.anchorX / (float)spec.width, 1 - spec.anchorY / (float)spec.height)
                       : new Vector2(.5f, .5f);
            if (anchor && source.bodies.ContainsKey(id))
            {
                var t = ForestRaster.Texture(shadow ? source.shadows[id] : source.bodies[id], spec.width,
                                             spec.height, key);
                NewAssets.Add(t);
                s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), pivot, 32, 0,
                                  SpriteMeshType.FullRect);
            }
            else
            {
                // 从紧密图集中隔离原生像素；Clamp 避免非整数缩放时相邻素材串色。
                var t = ForestRaster.Texture(source.Pixels(id), spec.width, spec.height, key);
                NewAssets.Add(t);
                s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), pivot, 32, 0,
                                  SpriteMeshType.FullRect);
            }
            s.name = key;
            sprites.Add(key, s);
            NewAssets.Add(s);
            return s;
        }

        Tile Tile(string id)
        {
            if (tiles.TryGetValue(id, out var t))
                return t;
            t = ScriptableObject.CreateInstance<Tile>();
            t.name = id;
            t.sprite = Source(id);
            t.colliderType = UnityEngine.Tilemaps.Tile.ColliderType.None;
            tiles.Add(id, t);
            NewAssets.Add(t);
            return t;
        }

        Tilemap Tilemap(Transform parent, string name, int order)
        {
            var go = Group(name, parent);
            var t = go.AddComponent<Tilemap>();
            var r = go.AddComponent<TilemapRenderer>();
            r.sortingOrder = order;
            return t;
        }

        SpriteRenderer DrawSprite(Sprite s, Vector2 position, int order, Transform parent)
        {
            var go = Group(s.name, parent);
            go.transform.position = position;
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = s;
            r.sortingOrder = order;
            return r;
        }

        Sprite RasterSprite(Color32[] pixels, int w, int h, string name)
        {
            var t = ForestRaster.Texture(pixels, w, h, name);
            NewAssets.Add(t);
            var s =
                Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 32, 0, SpriteMeshType.FullRect);
            s.name = name;
            NewAssets.Add(s);
            return s;
        }

        void AddRasterChunks(Color32[] pixels, int w, int h, Transform parent, int order)
        {
            for (int y = 0; y < h; y += 256)
                for (int x = 0; x < w; x += 256)
                {
                    int cw = Math.Min(256, w - x), ch = Math.Min(256, h - y);
                    bool any = false;
                    for (int py = 0; py < ch && !any; py++)
                        for (int px = 0; px < cw; px++)
                            if (pixels[(y + py) * w + x + px].a != 0)
                            {
                                any = true;
                                break;
                            }
                    if (!any)
                        continue;
                    var p = new Color32[cw * ch];
                    for (int py = 0; py < ch; py++)
                        Array.Copy(pixels, (y + py) * w + x, p, py * cw, cw);
                    DrawSprite(RasterSprite(p, cw, ch, "Surface " + x + "," + y),
                               World(x + cw / 2, y + ch / 2), order, parent);
                }
        }

        void Box(Transform parent, string name, Vector2 p, Vector2 size)
        {
            var go = Group(name, parent);
            go.transform.position = p;
            var c = go.AddComponent<BoxCollider2D>();
            c.size = size;
            Obstacles.Add(c);
        }

        public void FitCamera()
        {
            if (layout == null || mapCamera == null)
                return;
            mapCamera.orthographic = true;
            float panel =
                showControls && Application.isPlaying ? Mathf.Min(PanelWidth, Screen.width * .38f) : 0;
            mapCamera.rect =
                new Rect(panel / Mathf.Max(1, Screen.width), 0, 1 - panel / Mathf.Max(1, Screen.width), 1);
            mapCamera.ResetAspect();
            viewportWidth = Screen.width;
            viewportHeight = Screen.height;
            viewportControls = showControls;
            mapCamera.orthographicSize =
                closeView ? playerViewSize : Math.Max(18.5f, 24.5f / Math.Max(.1f, mapCamera.aspect));
            Vector2 focus = closeView ? CloseFocus() : new Vector2(24, -18);
            if (closeView)
            {
                float hy = mapCamera.orthographicSize, hx = Mathf.Min(24, hy * mapCamera.aspect);
                focus.x = Mathf.Clamp(focus.x, hx, 48 - hx);
                focus.y = Mathf.Clamp(focus.y, -36 + hy, -hy);
            }
            mapCamera.transform.position = new Vector3(focus.x, focus.y, -10);
        }

        Vector2 CloseFocus()
        {
            if (gridLayout.river != null)
                return World(768, gridLayout.river.bankBottom[768] + 48);
            if (layout.profileIndex == 3 && layout.clearings.Length > 0)
                return World(layout.clearings[0].x, layout.clearings[0].y);
            if (fencePlan == null || fencePlan.pieces.Length == 0)
                return World(layout.routes[0].points[layout.routes[0].points.Length / 2]);
            double x = 0, y = 0;
            int count = 0;
            foreach (var p in fencePlan.pieces)
                if (p.group == 0)
                {
                    x += p.rootX;
                    y += p.rootY;
                    count++;
                }
            x /= Math.Max(1, count);
            y /= Math.Max(1, count);
            var nearest = layout.routes[0].points[0];
            double best = double.MaxValue;
            foreach (var p in layout.routes[0].points)
            {
                double d = (p.x - x) * (p.x - x) + (p.y - y) * (p.y - y);
                if (d < best)
                {
                    best = d;
                    nearest = p;
                }
            }
            return World((int)((x + nearest.x) * .5), (int)((y + nearest.y) * .5));
        }

        public bool TryGenerateText(string text)
        {
            if (!int.TryParse(text, out int n))
            {
                message = "Seed 请输入有效整数";
                return false;
            }
            seed = n;
            try
            {
                Regenerate();
                return true;
            }
            catch (Exception e)
            {
                message = "生成失败，保留上一张地图：" + e.Message;
                Debug.LogException(e);
                return false;
            }
        }

        public void SetDiagnostics(int mode)
        {
            diagnosticMode = mode;
            if (overlay != null)
                Release(overlay);
            if (overlaySprite != null)
            {
                NewAssets.Remove(overlaySprite);
                Release(overlaySprite);
            }
            if (overlayTexture != null)
            {
                NewAssets.Remove(overlayTexture);
                Release(overlayTexture);
            }
            overlay = null;
            overlaySprite = null;
            overlayTexture = null;
            if (mode == 0 || layout == null)
                return;
            int w = layout.displayWidth, h = layout.displayHeight;
            var p = new Color32[w * h];
            for (int i = 0; i < p.Length; i++)
                p[i] = layout.navWalkable[i] != 0 ? new Color32(75, 210, 130, 65)
                                                  : new Color32(240, 150, 60, 155);
            var r = DrawSprite(RasterSprite(p, w, h, "Player clearance"), new Vector2(24, -18), 30000,
                               generated.transform);
            r.transform.localScale = new Vector3(16, 16, 1);
            r.gameObject.layer = 8;
            overlay = r.gameObject;
            overlaySprite = r.sprite;
            overlayTexture = r.sprite.texture;
        }

        void Update()
        {
            if (pending > 0)
            {
                if (--pending == 0)
                    TryGenerateText(seedText);
                return;
            }
            if (layout == null)
                return;
            if (viewportWidth != Screen.width || viewportHeight != Screen.height ||
                viewportControls != showControls)
                FitCamera();
            if (mapCamera != null && mapCamera.pixelRect.Contains(Input.mousePosition))
            {
                float wheel = Input.mouseScrollDelta.y;
                if (wheel != 0)
                    mapCamera.orthographicSize =
                        Mathf.Clamp(mapCamera.orthographicSize * Mathf.Pow(.88f, wheel), 3, 28);
                if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
                {
                    float s = 2 * mapCamera.orthographicSize / Screen.height;
                    mapCamera.transform.position += new Vector3(-Input.GetAxisRaw("Mouse X") * 12 * s,
                                                                -Input.GetAxisRaw("Mouse Y") * 12 * s, 0);
                }
            }
        }

        void OnGUI()
        {
            if (!showControls)
                return;
            if (skin == null)
            {
                skin = Instantiate(GUI.skin);
                uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 15);
                skin.font = uiFont;
                skin.label.fontSize = 14;
                skin.label.wordWrap = true;
                skin.button.fixedHeight = 32;
                skin.textField.fixedHeight = 32;
            }
            var previous = GUI.skin;
            GUI.skin = skin;
            float width = Mathf.Min(PanelWidth, Screen.width * .38f);
            var color = GUI.color;
            GUI.color = new Color(.12f, .16f, .14f, 1);
            GUI.DrawTexture(new Rect(0, 0, width, Screen.height), Texture2D.whiteTexture);
            GUI.color = color;
            GUILayout.BeginArea(new Rect(16, 16, width - 32, Screen.height - 32));
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("遗忘的记忆 / 乡野森林 V2");
            GUILayout.Label("主路与林间支路 · 原始像素素材");
            GUILayout.Space(16);
            GUILayout.Label("随机种子");
            seedText = GUILayout.TextField(seedText);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("重新生成"))
            {
                pending = 2;
                message = "准备生成…";
            }
            if (GUILayout.Button("新种子"))
            {
                seedText = unchecked(Environment.TickCount ^ seed * 397).ToString();
                pending = 2;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(12);
            GUILayout.Label("构图变化");
            style = GUILayout.SelectionGrid(style, Styles, 1);
            GUILayout.Space(8);
            GUILayout.Label("植被密度 " + vegetation.ToString("F2"));
            vegetation = GUILayout.HorizontalSlider(vegetation, .6f, 1.4f);
            GUILayout.Label("调整后点击重新生成");
            GUILayout.Space(12);
            withRiver = GUILayout.Toggle(withRiver, "生成宽窄变化的河流");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("全图"))
            {
                closeView = false;
                FitCamera();
            }
            if (GUILayout.Button("近景"))
            {
                closeView = true;
                FitCamera();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("滚轮缩放 · 右键 / 中键拖动");
            bool d = GUILayout.Toggle(diagnosticMode != 0, "显示角色通行净空");
            if (d != (diagnosticMode != 0))
                SetDiagnostics(d ? 1 : 0);
            GUILayout.Space(16);
            if (layout != null)
            {
                GUILayout.Label("48 × 36 格 · " + layout.validation.trees + " 棵树");
                GUILayout.Label(layout.validation.reachableNavCells + " 个连通位置");
                GUILayout.Label("路边整洁 · 林内野化");
                GUILayout.Label("地块 " + gridLayout.regionInfo.Length + " · 林内 " +
                                gridLayout.wildInteriorTiles + " 格");
            }
            GUILayout.Space(8);
            GUILayout.Label(message);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.skin = previous;
        }
    }
}
