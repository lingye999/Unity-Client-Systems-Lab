using System.IO;
using UnityEngine;

namespace ForgottenMemories.PastoralGridV2
{
    // 当前两套验收共同使用的截图工具，独立于任何旧场景和旧运行组件。
    public static class ForestCameraCapture
    {
        public static byte[] Capture(Camera camera, string path, int width, int height)
        {
            var previous = camera.targetTexture;
            var active = RenderTexture.active;
            float aspect = camera.aspect;
            var rect = camera.rect;
            var render = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32,
                                                    RenderTextureReadWrite.sRGB);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = render;
                camera.rect = new Rect(0, 0, 1, 1);
                camera.aspect = width / (float)height;
                camera.Render();
                RenderTexture.active = render;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                var bytes = texture.EncodeToPNG();
                if (path != null)
                {
                    File.WriteAllBytes(path, bytes);
                }
                return bytes;
            }
            finally
            {
                camera.targetTexture = previous;
                camera.rect = rect;
                camera.aspect = aspect;
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(render);
                Object.Destroy(texture);
            }
        }
    }
}
