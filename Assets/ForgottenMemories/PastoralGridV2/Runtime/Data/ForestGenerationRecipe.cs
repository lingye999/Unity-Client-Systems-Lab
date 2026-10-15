using System;

namespace ForgottenMemories.PastoralGridV2
{
    // 纯 C# 配方数据。Unity 资产负责编辑与保存，生成算法只读取本次快照。
    [Serializable]
    public sealed class ForestGenerationRecipe
    {
        public VegetationRecipe vegetation = new VegetationRecipe();
        public PlacementRecipe placement = new PlacementRecipe();

        public ForestGenerationRecipe Snapshot()
        {
            if (vegetation == null || placement == null)
                throw new ArgumentException("Recipe sections are required.");
            var result =
                new ForestGenerationRecipe { vegetation = vegetation.Copy(), placement = placement.Copy() };
            result.vegetation.Validate();
            result.placement.Validate();
            return result;
        }

        internal static void Range(double value, double min, double max, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
                throw new ArgumentOutOfRangeException(name, "Recipe value is outside its supported range.");
        }
    }

    [Serializable]
    public sealed class VegetationRecipe
    {
        // 尺度使用原图像素；宏观场组织林团，局部场打散区域内部的疏密。
        public double macroScalePixels = 410;
        public double localScalePixels = 145;

        public double baseDensity = .39;
        public double edgeWeight = .21;
        public double macroWeight = .20;
        public double localWeight = .10;
        public double regionWeight = .08;
        public double regionReference = .45;
        public double maintenanceSuppression = .89;

        // 空地和道路先影响植被权重，最终落点仍必须通过硬约束。
        public double edgeStartPixels = 50;
        public double edgeFadePixels = 230;
        public double roadFadePixels = 85;
        public double clearingMarginPixels = 30;

        internal VegetationRecipe Copy()
        {
            return (VegetationRecipe)MemberwiseClone();
        }

        internal void Validate()
        {
            ForestGenerationRecipe.Range(macroScalePixels, 32, 4096, "macroScalePixels");
            ForestGenerationRecipe.Range(localScalePixels, 32, 4096, "localScalePixels");
            ForestGenerationRecipe.Range(baseDensity, 0, 1, "baseDensity");
            ForestGenerationRecipe.Range(edgeWeight, 0, 1, "edgeWeight");
            ForestGenerationRecipe.Range(macroWeight, 0, 1, "macroWeight");
            ForestGenerationRecipe.Range(localWeight, 0, 1, "localWeight");
            ForestGenerationRecipe.Range(regionWeight, 0, 1, "regionWeight");
            ForestGenerationRecipe.Range(regionReference, 0, 1, "regionReference");
            ForestGenerationRecipe.Range(maintenanceSuppression, 0, 1, "maintenanceSuppression");
            ForestGenerationRecipe.Range(edgeStartPixels, 0, 256, "edgeStartPixels");
            ForestGenerationRecipe.Range(edgeFadePixels, 1, 1024, "edgeFadePixels");
            ForestGenerationRecipe.Range(roadFadePixels, 1, 512, "roadFadePixels");
            ForestGenerationRecipe.Range(clearingMarginPixels, 30, 128, "clearingMarginPixels");
        }
    }

    [Serializable]
    public sealed class PlacementRecipe
    {
        // 以下距离均为原图像素，不是 Unity 世界单位。
        public int boundaryMarginPixels = 6;
        public int rootRoadPaddingPixels = 9;
        public int rootClearingMarginPixels = 12;
        public int rootBankPaddingPixels = 4;
        public int canopyClearingMarginPixels = 8;
        public int treeRootGapPixels = 10;
        public int propRootGapPixels = 6;
        public int willowSpacingPixels = 320;
        public double maxTreeCanopyOverlap = .45;

        internal PlacementRecipe Copy()
        {
            return (PlacementRecipe)MemberwiseClone();
        }

        internal void Validate()
        {
            // 可以增加安全余量，但不能通过改配方静默降低现用的通行净空。
            ForestGenerationRecipe.Range(boundaryMarginPixels, 6, 128, "boundaryMarginPixels");
            ForestGenerationRecipe.Range(rootRoadPaddingPixels, 9, 128, "rootRoadPaddingPixels");
            ForestGenerationRecipe.Range(rootClearingMarginPixels, 12, 128, "rootClearingMarginPixels");
            ForestGenerationRecipe.Range(rootBankPaddingPixels, 4, 128, "rootBankPaddingPixels");
            ForestGenerationRecipe.Range(canopyClearingMarginPixels, 8, 128, "canopyClearingMarginPixels");
            ForestGenerationRecipe.Range(treeRootGapPixels, 10, 128, "treeRootGapPixels");
            ForestGenerationRecipe.Range(propRootGapPixels, 6, 128, "propRootGapPixels");
            ForestGenerationRecipe.Range(willowSpacingPixels, 320, 1024, "willowSpacingPixels");
            ForestGenerationRecipe.Range(maxTreeCanopyOverlap, 0, .45, "maxTreeCanopyOverlap");
        }
    }
}
