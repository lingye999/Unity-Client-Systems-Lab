using System;
using System.Collections.Generic;
namespace ForgottenMemories.PastoralGridV2
{
    /// <summary>一次候选落点；范围均为原图像素，不使用树冠矩形代替根部碰撞。</summary>
    public struct PropPlacement
    {
        public readonly SpriteSpec Sprite;
        public readonly int X;
        public readonly int Y;
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;

        public PropPlacement(SpriteSpec sprite, int x, int y)
        {
            Sprite = sprite;
            X = x;
            Y = y;
            Left = x - sprite.anchorX;
            Top = y - sprite.anchorY;
            Right = Left + sprite.width;
            Bottom = Top + sprite.height;
        }
    }

    /// <summary>同一次生成共享上下文；规则只读取，已接受对象由生成器追加。</summary>
    public sealed class PlacementContext
    {
        public readonly PastoralLayout Layout;
        public readonly PlacementRecipe Policy;
        public readonly IReadOnlyList<Prop> Placed;
        public readonly IReadOnlyDictionary<string, SpriteSpec> Sprites;

        internal PlacementContext(PastoralLayout layout, PlacementRecipe policy, List<Prop> placed,
                                  Dictionary<string, SpriteSpec> sprites)
        {
            Layout = layout;
            Policy = policy;
            Placed = placed;
            Sprites = sprites;
        }
    }

    public interface IPlacementConstraint
    {
        string Id { get; }
        bool Allows(PlacementContext context, PropPlacement candidate);
    }

    [Serializable]
    public sealed class PlacementRuleStatistics
    {
        public string ruleId;
        public int rejected;
    }

    /// <summary>
    /// 硬约束按固定顺序组合；首个失败即拒绝。附加规则只能收紧条件，不能覆盖内置规则。
    /// 热路径使用值类型候选和计数数组，避免每次采样分配对象或拼接错误字符串。
    /// </summary>
    public sealed class PlacementConstraintSet
    {
        private readonly PlacementContext context;
        private readonly IPlacementConstraint[] rules;
        private readonly int[] rejected;

        internal PlacementConstraintSet(PlacementContext context, IPlacementConstraint[] additional)
        {
            this.context = context;
            var ordered =
                new List<IPlacementConstraint> { new MapBoundsConstraint(), new RootCorridorConstraint(),
                                                 new DryRootConstraint(), new CanopyCorridorConstraint(),
                                                 new PropSpacingConstraint() };
            if (additional != null)
            {
                ordered.AddRange(additional);
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in ordered)
            {
                if (rule == null || string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id))
                {
                    throw new ArgumentException("Placement rules require unique nonempty IDs.");
                }
            }

            rules = ordered.ToArray();
            rejected = new int[rules.Length];
        }

        public bool Allows(SpriteSpec sprite, int x, int y)
        {
            var candidate = new PropPlacement(sprite, x, y);
            for (int i = 0; i < rules.Length; i++)
            {
                if (!rules[i].Allows(context, candidate))
                {
                    rejected[i]++;
                    return false;
                }
            }
            return true;
        }

        public PlacementRuleStatistics[] SnapshotStatistics()
        {
            var result = new PlacementRuleStatistics[rules.Length];
            for (int i = 0; i < rules.Length; i++)
            {
                result[i] = new PlacementRuleStatistics { ruleId = rules[i].Id, rejected = rejected[i] };
            }
            return result;
        }
    }

    internal sealed class MapBoundsConstraint : IPlacementConstraint
    {
        public string Id
        {
            get {
                return "map-bounds";
            }
        }

        public bool Allows(PlacementContext context, PropPlacement p)
        {
            int margin = context.Policy.boundaryMarginPixels;
            return p.Left >= margin && p.Top >= margin && p.Right < ForestMapMetrics.PixelWidth - margin &&
                   p.Bottom < ForestMapMetrics.PixelHeight - margin;
        }
    }

    internal sealed class RootCorridorConstraint : IPlacementConstraint
    {
        public string Id
        {
            get {
                return "root-corridor";
            }
        }

        public bool Allows(PlacementContext context, PropPlacement p)
        {
            var map = context.Layout.map;
            double radius = p.Sprite.radiusPixels;
            return PastoralGridGenerator.DistanceToRoad(map, p.X, p.Y) >=
                       radius + ForestMapMetrics.PlayerRadiusPixels + context.Policy.rootRoadPaddingPixels &&
                   !PastoralGridGenerator.IsClearing(map, p.X, p.Y,
                                                     radius + context.Policy.rootClearingMarginPixels);
        }
    }

    internal sealed class DryRootConstraint : IPlacementConstraint
    {
        public string Id
        {
            get {
                return "dry-root";
            }
        }

        public bool Allows(PlacementContext context, PropPlacement p)
        {
            return PastoralRiverGenerator.DryDisk(
                context.Layout, p.X, p.Y, p.Sprite.radiusPixels + context.Policy.rootBankPaddingPixels);
        }
    }

    internal sealed class CanopyCorridorConstraint : IPlacementConstraint
    {
        public string Id
        {
            get {
                return "canopy-corridor";
            }
        }

        public bool Allows(PlacementContext context, PropPlacement p)
        {
            for (int y = p.Top + 3; y < p.Bottom; y += 12)
            {
                for (int x = p.Left + 3; x < p.Right; x += 12)
                {
                    if (PastoralGridGenerator.DistanceToRoad(context.Layout.map, x, y) < 1 ||
                        PastoralGridGenerator.IsClearing(context.Layout.map, x, y,
                                                         context.Policy.canopyClearingMarginPixels) ||
                        (context.Layout.river != null &&
                         PastoralRiverGenerator.DistanceToWater(context.Layout, x, y) < -12))
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }

    internal sealed class PropSpacingConstraint : IPlacementConstraint
    {
        public string Id
        {
            get {
                return "prop-spacing";
            }
        }

        public bool Allows(PlacementContext context, PropPlacement p)
        {
            foreach (var placed in context.Placed)
            {
                var other = context.Sprites[placed.id];
                bool bothTrees = p.Sprite.family == "tree" && other.family == "tree";
                double gap = bothTrees ? context.Policy.treeRootGapPixels : context.Policy.propRootGapPixels;
                double minimum = p.Sprite.radiusPixels + other.radiusPixels + gap;
                if (p.Sprite.id == "tree_willow" && other.id == "tree_willow")
                {
                    minimum = context.Policy.willowSpacingPixels;
                }

                double dx = p.X - placed.rootX;
                double dy = p.Y - placed.rootY;
                if (Math.Sqrt(dx * dx + dy * dy) < minimum)
                {
                    return false;
                }

                if (bothTrees)
                {
                    int left = placed.rootX - other.anchorX;
                    int top = placed.rootY - other.anchorY;
                    int overlap =
                        Math.Max(0, Math.Min(p.Right, left + other.width) - Math.Max(p.Left, left)) *
                        Math.Max(0, Math.Min(p.Bottom, top + other.height) - Math.Max(p.Top, top));
                    if (overlap > Math.Min(p.Sprite.width * p.Sprite.height, other.width * other.height) *
                                      context.Policy.maxTreeCanopyOverlap)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
