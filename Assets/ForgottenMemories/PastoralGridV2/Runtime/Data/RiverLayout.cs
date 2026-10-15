using System;
namespace ForgottenMemories.PastoralGridV2
{
    [Serializable]
    public sealed class RiverLayout
    {
        public int sampleWidth = 49, sampleHeight = 37, sampleStep = 32;
        public int[] waterSamples, waterMasks;
        public int[] centerY, bankTop, bankBottom;
        public Point[] centerline;
        public int[] widths;
        public int minWidthPixels, maxWidthPixels, ambiguityCorrections, wetTileCount, shoreTileCount;
        public int bankProtectionPixels = 8;
        public string direction = "west_to_east", playableBank = "south";
        // 最终岸线同时提供碰撞与导航几何，后续系统不再读取量化前的噪声曲线。
        public Point[] shoreA, shoreB;
    }
}
