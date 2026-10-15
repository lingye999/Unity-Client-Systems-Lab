using UnityEngine;
using UnityEngine.Tilemaps;

namespace ForgottenMemories.PastoralGridV2
{
    // Serialized native frames remain visible in the saved editor scene and
    // animate through Tilemap's shared clock rather than per-cell Update calls.
    public sealed class PastoralRiverTile : TileBase
    {
        public Sprite[] frames;
        public int waterMask, variant, phase;

        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData data)
        {
            data.sprite = frames != null && frames.Length > 0 ? frames[0] : null;
            data.color = Color.white;
            data.transform = Matrix4x4.identity;
            data.flags = TileFlags.LockAll;
            data.colliderType = Tile.ColliderType.None;
        }

        public override bool GetTileAnimationData(Vector3Int position, ITilemap tilemap,
                                                  ref TileAnimationData data)
        {
            if (frames == null || frames.Length < 2)
                return false;
            data.animatedSprites = frames;
            data.animationSpeed = 1;
            data.animationStartTime = 0;
            return true;
        }
    }
}
