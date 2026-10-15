using System;
namespace ForgottenMemories.PastoralGridV2
{
    [Serializable]
    public sealed class RegionInfo
    {
        public int id, tileCount, perlinSeed, centerX, centerY;
        public string kind;
        public double meanMaintenance, vegetationTarget;
    }

    [Serializable]
    public sealed class RoadInfo
    {
        public int routeIndex, fromNode, toNode, halfWidthPixels;
        public string hierarchy;
        public bool closesLoop;
    }

    [Serializable]
    public sealed class RoadNode
    {
        public int id, x, y;
        public string kind;
    }

    [Serializable]
    public sealed class RoadEdge
    {
        public int from, to, routeIndex, lengthPixels;
        public string hierarchy;
    }

    [Serializable]
    public sealed class PastoralLayout
    {
        public Map map;
        public RiverLayout river;
        // 共享角位从左上顺时针为 1、2、4、8；位值为 1 表示泥土。
        public int sampleWidth = 49, sampleHeight = 37, sampleStep = 32;
        public int[] soilSamples, soilMasks, regions, roadClass, maintenance;
        public bool scenicPaths;
        // 路线地表独立于通行：0 为土路，1 为保留净空的草地游径。
        public int[] routeSurface;
        public RegionInfo[] regionInfo;
        public RoadInfo[] roadInfo;
        public RoadNode[] roadNodes;
        public RoadEdge[] roadEdges;
        public int roadGraphCycles;
        public int ambiguityCorrections, wildInteriorTiles, loopDetourPixels;
        public int greenTrees, warmTrees, cyanTrees, shrubCount, shrubColonies;
        public double greenTreeFraction, warmTreeFraction, cyanTreeFraction;
    }
}
