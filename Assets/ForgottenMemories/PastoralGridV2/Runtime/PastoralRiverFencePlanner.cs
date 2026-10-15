using System.Collections.Generic;
namespace ForgottenMemories.PastoralGridV2
{
    public static class PastoralRiverFencePlanner
    {
        public static FencePlan Generate(PastoralLayout layout, Catalog catalog)
        {
            var original = ScenicFencePlanner.Generate(layout, catalog);
            if (layout.river == null)
                return original;
            // Remove an entire connected group if it reaches a bank. Removing
            // individual pieces would leave an incorrect endcap or corner.
            var valid = new bool[original.groups.Length];
            for (int i = 0; i < valid.Length; i++)
                valid[i] = true;
            foreach (var piece in original.pieces)
                if (!PastoralRiverGenerator.DryDisk(layout, piece.rootX, piece.rootY, 14))
                    valid[piece.group] = false;
            foreach (var barrier in original.barriers)
            {
                if (PastoralRiverGenerator.DrySweep(layout, barrier.a, barrier.b, barrier.radius + 6))
                    continue;
                foreach (var piece in original.pieces)
                    if (piece.rootX == barrier.a.x && piece.rootY == barrier.a.y)
                        valid[piece.group] = false;
            }
            var groups = new List<FenceGroup>();
            var pieces = new List<FencePiece>();
            var barriers = new List<FenceBarrier>();
            var ids = new int[valid.Length];
            for (int i = 0; i < valid.Length; i++)
            {
                ids[i] = -1;
                if (valid[i])
                {
                    ids[i] = groups.Count;
                    groups.Add(original.groups[i]);
                }
            }
            foreach (var piece in original.pieces)
                if (valid[piece.group])
                    pieces.Add(new FencePiece { id = piece.id, rootX = piece.rootX, rootY = piece.rootY,
                                                    group = ids[piece.group] });
            foreach (var barrier in original.barriers)
                foreach (var piece in original.pieces)
                    if (piece.rootX == barrier.a.x && piece.rootY == barrier.a.y)
                    {
                        if (valid[piece.group])
                            barriers.Add(barrier);
                        break;
                    }
            return new FencePlan { pieces = pieces.ToArray(), barriers = barriers.ToArray(),
                                       groups = groups.ToArray() };
        }
    }
}
