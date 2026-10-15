using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace ForgottenMemories.PastoralGridV2
{
    // 保留原脚本 GUID，并声明旧命名空间，现用场景的根部排序引用继续有效。
    [MovedFrom("ForgottenMemories.ForestWorkbench")]
    [ExecuteAlways, RequireComponent(typeof(SpriteRenderer))]
    public sealed class ForestDepth : MonoBehaviour
    {
        SpriteRenderer cached;

        void OnEnable()
        {
            cached = GetComponent<SpriteRenderer>();
        }

        void LateUpdate()
        {
            if (cached != null)
                cached.sortingOrder = Order(transform.position.y) + 1;
        }

        public static int Order(float worldY)
        {
            return Mathf.Clamp(Mathf.RoundToInt(-worldY * 512), -16000, 28000);
        }
    }
}
