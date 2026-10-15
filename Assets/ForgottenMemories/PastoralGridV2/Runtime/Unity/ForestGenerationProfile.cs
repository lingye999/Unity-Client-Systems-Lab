using UnityEngine;

namespace ForgottenMemories.PastoralGridV2
{
    /// <summary>Inspector 配置入口；资产只存参数，生成算法使用独立的纯 C# 快照。</summary>
    [CreateAssetMenu(menuName = "Forgotten Memories/Forest Generation Profile", fileName = "ForestProfile")]
    public sealed class ForestGenerationProfile : ScriptableObject
    {
        [Tooltip("当前森林配方。道路和角色净空的安全下限不能降低。")]
        public ForestGenerationRecipe recipe = new ForestGenerationRecipe();

        public ForestGenerationRecipe CreateSnapshot()
        {
            return (recipe ?? new ForestGenerationRecipe()).Snapshot();
        }
    }
}
