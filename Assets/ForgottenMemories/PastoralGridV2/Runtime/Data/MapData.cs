using System;

namespace ForgottenMemories.PastoralGridV2
{
    [Serializable]
    public sealed class SpriteSpec
    {
        public string id, family;
        public int x, y, width, height, anchorX, anchorY, radiusPixels;
    }

    [Serializable]
    public sealed class Catalog
    {
        public SpriteSpec[] sprites;
    }

    [Serializable]
    public struct Point
    {
        public int x, y;

        public Point(int a, int b)
        {
            x = a;
            y = b;
        }
    }

    [Serializable]
    public sealed class Stamp
    {
        public string id;
        public int x, y, role, sortY;
    }

    [Serializable]
    public sealed class Prop
    {
        public string id, instanceId;
        public int rootX, rootY, level, group;
    }

    [Serializable]
    public sealed class Clearing
    {
        public int x, y, rx, ry;
        public Point[] outline;
    }

    [Serializable]
    public sealed class Route
    {
        public string kind;
        public Point[] points;
        public int[] radii;
    }

    [Serializable]
    public sealed class Paver
    {
        public int x, y, variant;
    }

    [Serializable]
    public sealed class Highland
    {
        // The rectangle is only a sampling bound. These column profiles own the
        // actual rim/grass/front footprint; no rectangle is sent to physics.
        public int x, y, width, height;
        public int[] backRows, frontRows;
        public bool joinsNorthBoundary, joinsWestBoundary, joinsEastBoundary, low;
    }

    [Serializable]
    public sealed class Fall
    {
        public int x, y, width, height = 4, upstreamLevel = 1, downstreamLevel = 0;
    }

    [Serializable]
    public sealed class Grove
    {
        public int x, y, radius, wanted, accepted;
        public string species;
    }

    [Serializable]
    public sealed class Port
    {
        public string kind, edge, direction, units = "pixels";
        public int offset, width, level;
        public bool connectedToBoundary;
    }

    [Serializable]
    public sealed class Report
    {
        public bool passed;
        public string[] failures;
        public int reachableNavCells, trees, groves, props, mainRoutes, trails, minRiverWidth, maxRiverWidth,
            riverWidthPixelsMin, riverWidthPixelsMax;
    }

    // 保留现有烘焙 JSON 的字段与顺序；数据类型不依赖旧生成器或 Unity。
    [Serializable]
    public sealed class Map
    {
        public string algorithmVersion = "forest-workbench-1.2", profile;
        public float vegetation = 1f;
        public int seed, profileIndex, width = 48, height = 36, tilePixels = 32, navStep = 16;
        public int[] shoreMasks, terrainSurface, terrainBlocked, groundVariant, density, navWalkable,
            navLinks;
        public int[] habitat, waterVariant;
        public int[] riverLeft, riverRight, riverCenterPixels;
        public int displayStep = 16, displayWidth = 96, displayHeight = 72, logicalOriginX = -8,
                   logicalOriginY = -8;
        public int[] logicalLand, displayShoreMasks, displayBlocked;
        public int[] bankLeftPixels, bankRightPixels;
        public int bankProtectionPixels = 8;
        public double[] waterElevation;
        public Highland[] highlands;
        public Fall waterfall;
        public Stamp[] terrainSprites;
        public Route[] routes;
        public Clearing[] clearings;
        public Point entrance, exit;
        public Point[] collectibles;
        public Prop[] props;
        public Grove[] groves;
        public Paver[] pavers;
        public Port[] ports;
        public Report validation;
        internal double[] routeClearance;
    }
}
