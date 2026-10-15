using System;

namespace ForgottenMemories.PastoralGridV2
{
    // 单次生成的只读输入；几何坐标与距离统一使用原图像素。
    public sealed class MapGenerationRequest
    {
        public readonly int Seed, Style;
        public readonly float Vegetation;
        public readonly bool WithRiver;

        public MapGenerationRequest(int seed, int style, float vegetation = 1f, bool withRiver = false)
        {
            if (style > 3)
                throw new ArgumentOutOfRangeException("style");
            if (float.IsNaN(vegetation) || float.IsInfinity(vegetation))
                throw new ArgumentOutOfRangeException("vegetation", "Vegetation must be finite.");
            Seed = seed;
            Style = style < 0 ? (int)(unchecked((uint)seed) % 3) : style;
            Vegetation = Math.Max(.6f, Math.Min(1.4f, vegetation));
            WithRiver = withRiver;
        }
    }

    // 这些尺寸属于当前素材与导航约定；修改它们并不能直接获得任意大小或无限地图。
    public static class ForestMapMetrics
    {
        public const int Width = 48, Height = 36, TilePixels = 32, NavigationStep = 16;
        public const int PixelWidth = Width * TilePixels, PixelHeight = Height * TilePixels;
        public const double PlayerRadiusPixels = 9.6;
    }
}
