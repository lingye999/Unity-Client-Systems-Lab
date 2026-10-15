using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ForgottenMemories.PastoralGridV2
{
    public sealed class NativeFenceAssets
    {
        readonly Texture2D atlas;
        readonly Dictionary<string, SpriteSpec> specs = new Dictionary<string, SpriteSpec>();

        // The runtime owns generated sprites. Do not cache across regeneration.
        public NativeFenceAssets()
        {
            atlas = Resources.Load<Texture2D>("ForestMap/fences");
            var text = Resources.Load<TextAsset>("ForestMap/fences");
            if (atlas == null || text == null)
                throw new InvalidOperationException("Native fence resource missing.");
            foreach (var s in JsonUtility.FromJson<Catalog>(text.text).sprites)
                specs.Add(s.id, s);
        }

        public Sprite Sprite(string id, List<Object> owned)
        {
            foreach (var o in owned)
                if (o is Sprite sprite && sprite.name == id)
                    return sprite;
            var s = specs[id];
            var texture = new Texture2D(s.width, s.height, TextureFormat.RGBA32,
                                        false) { name = id + " pixels", filterMode = FilterMode.Point,
                                                 wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels(atlas.GetPixels(s.x, atlas.height - s.y - s.height, s.width, s.height));
            texture.Apply(false, false);
            owned.Add(texture);
            var result = UnityEngine.Sprite.Create(
                texture, new Rect(0, 0, s.width, s.height),
                new Vector2(s.anchorX / (float)s.width, 1 - s.anchorY / (float)s.height), 32, 0,
                SpriteMeshType.FullRect);
            result.name = id;
            owned.Add(result);
            return result;
        }
    }
}
