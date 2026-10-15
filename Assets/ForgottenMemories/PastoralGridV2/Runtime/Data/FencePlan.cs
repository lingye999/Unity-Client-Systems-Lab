using System;

namespace ForgottenMemories.PastoralGridV2
{
    // 障碍半径是原图像素中的半厚度，导航再叠加玩家半径。
    [Serializable]
    public sealed class FenceBarrier
    {
        public Point a, b;
        public double radius = 4;
    }

    [Serializable]
    public sealed class FencePiece
    {
        public string id;
        public int rootX, rootY, group;
    }

    [Serializable]
    public sealed class FenceGroup
    {
        public string kind;
        public int pieces;
    }

    [Serializable]
    public sealed class FencePlan
    {
        public FencePiece[] pieces;
        public FenceBarrier[] barriers;
        public FenceGroup[] groups;
    }

}
