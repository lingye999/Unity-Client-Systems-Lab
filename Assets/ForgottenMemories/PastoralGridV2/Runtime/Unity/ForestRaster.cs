using System;
using System.Collections.Generic;
using UnityEngine;

namespace ForgottenMemories.PastoralGridV2
{
    /// <summary>原图像素适配层；仅拆分原有阴影，不负责地形或道路生成。</summary>
    public sealed class ForestRaster
    {
        public readonly Dictionary<string, SpriteSpec> specs = new Dictionary<string, SpriteSpec>();
        readonly Dictionary<string, Color32[]> pixels = new Dictionary<string, Color32[]>();
        public readonly Texture2D atlas;
        public readonly Dictionary<string, Color32[]> bodies = new Dictionary<string, Color32[]>();
        public readonly Dictionary<string, Color32[]> shadows = new Dictionary<string, Color32[]>();

        public ForestRaster(Catalog catalog, Texture2D source)
        {
            atlas = source;
            var raw = source.GetPixels32();
            foreach (var s in catalog.sprites)
            {
                specs.Add(s.id, s);
                var p = new Color32[s.width * s.height];
                for (int y = 0; y < s.height; y++)
                    Array.Copy(raw, (source.height - 1 - s.y - y) * source.width + s.x, p, y * s.width,
                               s.width);
                pixels.Add(s.id, p);
                if (s.family == "tree" || s.family == "sapling" || s.family == "bush" || s.family == "rock" ||
                    s.family == "stump")
                {
                    var body = (Color32[])p.Clone();
                    var shade = new Color32[p.Length];
                    for (int i = 0; i < p.Length; i++)
                        if (p[i].r == 44 && p[i].g == 33 && p[i].b == 51 && p[i].a == 125)
                        {
                            shade[i] = p[i];
                            body[i] = new Color32();
                        }
                    bodies.Add(s.id, body);
                    shadows.Add(s.id, shade);
                }
            }
        }

        public Color32[] Pixels(string id)
        {
            return pixels[id];
        }

        public static Color32 Over(Color32 dst, Color32 src)
        {
            if (src.a == 255)
                return src;
            if (src.a == 0)
                return dst;
            float sa = src.a / 255f, da = dst.a / 255f, a = sa + da * (1 - sa);
            return new Color32((byte)Mathf.RoundToInt((src.r * sa + dst.r * da * (1 - sa)) / a),
                               (byte)Mathf.RoundToInt((src.g * sa + dst.g * da * (1 - sa)) / a),
                               (byte)Mathf.RoundToInt((src.b * sa + dst.b * da * (1 - sa)) / a),
                               (byte)Mathf.RoundToInt(a * 255));
        }

        public static Texture2D Texture(Color32[] topDown, int w, int h, string name)
        {
            var bottomUp = new Color32[topDown.Length];
            for (int y = 0; y < h; y++)
                Array.Copy(topDown, y * w, bottomUp, (h - y - 1) * w, w);
            var texture =
                new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Point,
                                                                   wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(bottomUp);
            texture.Apply(false, false);
            return texture;
        }
    }
}
