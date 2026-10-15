using UnityEngine;

namespace ForgottenMemories.PastoralGridV2
{
    public sealed class PastoralGridV2Bake : ScriptableObject
    {
        public int seed, style;
        public string algorithmVersion;
        public string source = "Original ForestGen1 atlas and native FlatForest fence PNG crops";
    }
}
