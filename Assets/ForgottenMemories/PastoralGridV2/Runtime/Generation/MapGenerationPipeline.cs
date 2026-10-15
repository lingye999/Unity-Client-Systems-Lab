using System;
namespace ForgottenMemories.PastoralGridV2
{
    public sealed class MapGenerationResult
    {
        public readonly PastoralLayout Layout;
        public readonly FencePlan Fences;
        public readonly PlacementRuleStatistics[] PlacementStatistics;

        internal MapGenerationResult(PastoralLayout layout, FencePlan fences,
                                     PlacementRuleStatistics[] placementStatistics)
        {
            Layout = layout;
            Fences = fences;
            PlacementStatistics = placementStatistics;
        }
    }

    /// <summary>
    /// 纯 C# 生成入口。每次生成只持有自己的配置快照、布局和规则计数，不读取 Unity 资源。
    /// 固定顺序保证水岸先于道路，最终导航先于场景展示。
    /// </summary>
    public static class MapGenerationPipeline
    {
        public static MapGenerationResult Build(MapGenerationRequest request, Catalog catalog,
                                                ForestGenerationRecipe recipe = null,
                                                params IPlacementConstraint[] additionalConstraints)
        {
            var result = BuildLayout(request, catalog, recipe, additionalConstraints);
            var fences = PastoralRiverFencePlanner.Generate(result.Layout, catalog);
            PastoralGridGenerator.RebuildNavigation(result.Layout, catalog, fences.barriers);
            EnsureValid(result.Layout, catalog);
            return new MapGenerationResult(result.Layout, fences, result.PlacementStatistics);
        }

        public static MapGenerationResult BuildLayout(MapGenerationRequest request, Catalog catalog,
                                                      ForestGenerationRecipe recipe = null,
                                                      params IPlacementConstraint[] additionalConstraints)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (catalog == null || catalog.sprites == null)
            {
                throw new ArgumentException("An audited native PNG catalog is required.", nameof(catalog));
            }

            var settings = (recipe ?? new ForestGenerationRecipe()).Snapshot();
            var layout = PastoralGridGenerator.CreateLayout(request);
            if (request.WithRiver)
            {
                PastoralRiverGenerator.Generate(layout);
            }
            PastoralGridGenerator.ComposeRoads(layout);
            PastoralRiverGenerator.AppendPorts(layout);
            PastoralGridGenerator.PrepareRoadDistance(layout.map);
            PastoralGridGenerator.BuildDualGrid(layout);
            PastoralGridGenerator.BuildRegions(layout);
            var statistics = PastoralGridGenerator.Plant(layout, catalog, settings, additionalConstraints);
            PastoralGridGenerator.RebuildNavigation(layout, catalog);
            EnsureValid(layout, catalog);
            return new MapGenerationResult(layout, null, statistics);
        }

        private static void EnsureValid(PastoralLayout layout, Catalog catalog)
        {
            layout.map.validation = PastoralGridGenerator.Validate(layout, catalog);
            if (!layout.map.validation.passed)
            {
                throw new InvalidOperationException(layout.map.profile + " / " + layout.map.seed + ": " +
                                                    string.Join("; ", layout.map.validation.failures));
            }
        }
    }
}
