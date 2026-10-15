using System;
using System.IO;
using ForgottenMemories.PastoralGridV2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForgottenMemories.PastoralGridV2.Editor
{
    public static class PastoralGridV2Editor
    {
        public const string Root = "Assets/ForgottenMemories/PastoralGridV2";
        public const string ScenePath = Root + "/Scenes/RiverWoodlandV2.unity";

        static void RequireSaved()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException(
                        "请先保存当前场景，再打开双网格森林。未保存内容不会自动关闭。");
        }

        [MenuItem("Forgotten Memories/Pastoral Grid V2/Open current forest")]
        public static void Open()
        {
            RequireSaved();
            PastoralRiverEditor.Open();
            Frame();
        }

        internal static void Configure()
        {
            foreach (var name in new[] { "fences", "paving_dense" })
            {
                string path = "Assets/ForgottenMemories/PastoralGridV2/Resources/ForestMap/" + name + ".png";
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var t = (TextureImporter)AssetImporter.GetAtPath(path);
                if (t == null)
                    throw new IOException("Native fence PNG not imported.");
                t.textureType = TextureImporterType.Default;
                t.isReadable = true;
                t.filterMode = FilterMode.Point;
                t.mipmapEnabled = false;
                t.npotScale = TextureImporterNPOTScale.None;
                t.textureCompression = TextureImporterCompression.Uncompressed;
                t.alphaIsTransparency = true;
                t.wrapMode = TextureWrapMode.Clamp;
                t.SaveAndReimport();
            }
        }

        public static void Bake(PastoralGridV2Runtime r)
        {
            var asset = ScriptableObject.CreateInstance<PastoralGridV2Bake>();
            asset.seed = r.seed;
            asset.style = r.style;
            asset.algorithmVersion = r.Layout.algorithmVersion;
            Directory.CreateDirectory(Root + "/Baked");
            AssetDatabase.CreateAsset(asset,
                                      AssetDatabase.GenerateUniqueAssetPath(
                                          Root + "/Baked/Pastoral_" + r.seed + "_" + r.style + ".asset"));
            foreach (var o in r.NewAssets)
                if (o != null && !EditorUtility.IsPersistent(o))
                    AssetDatabase.AddObjectToAsset(o, asset);
            AssetDatabase.SaveAssets();
            r.ForgetBakedAssetOwnership();
            EditorUtility.SetDirty(r);
            EditorSceneManager.MarkSceneDirty(r.gameObject.scene);
        }

        static void Frame()
        {
            var r = UnityEngine.Object.FindObjectOfType<PastoralGridV2Runtime>();
            if (r == null)
                return;
            r.FitCamera();
            Selection.activeGameObject = r.gameObject;
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.in2DMode = true;
                SceneView.lastActiveSceneView.LookAt(new Vector3(24, -18, 0), Quaternion.identity, 20, true,
                                                     true);
            }
        }

        [MenuItem("Forgotten Memories/Pastoral Grid V2/Run acceptance")]
        public static void Acceptance()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Stop Play Mode first.");
            Open();
            string output = AcceptanceOutput("Dry");
            Directory.CreateDirectory(output);
            SessionState.SetString("PastoralGridV2Output", output);
            SessionState.SetString("PastoralGridV2Deadline", DateTime.UtcNow.AddMinutes(8).ToString("O"));
            SessionState.SetBool("PastoralGridV2Testing", true);
            EditorApplication.isPlaying = true;
        }

        internal static string AcceptanceOutput(string variant)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-forestAcceptanceOutput")
                {
                    return Path.GetFullPath(args[i + 1]);
                }
            }
            return Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../output/ForestCurrentAcceptance/" + variant));
        }

        public static void BatchVerify()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Requires batch mode.");
            try
            {
                // 测试参数由 Play Mode 验收组件设置，运行前不修改或保存当前场景。
                SessionState.SetBool("PastoralGridV2Batch", true);
                Acceptance();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(2);
            }
        }
    }

    [CustomEditor(typeof(PastoralGridV2Runtime))]
    public sealed class PastoralGridV2Inspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var r = (PastoralGridV2Runtime)target;
            EditorGUILayout.HelpBox("Perlin 环境场 → 连接图与 A* → 地块维护场 → 双网格草土。地表是原尺寸 " +
                                        "Tilemap，树木与栏杆可单独编辑。手工改瓦片仅改变画面，重新生成会覆" +
                                        "盖；保留调整请另存场景。",
                                    MessageType.Info);
            if (GUILayout.Button("重新生成 / Regenerate"))
            {
                try
                {
                    r.Regenerate();
                    if (!Application.isPlaying)
                        PastoralGridV2Editor.Bake(r);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            if (GUILayout.Button("全图 / 近景"))
            {
                r.closeView = !r.closeView;
                r.FitCamera();
            }
        }
    }

    [InitializeOnLoad]
    public static class PastoralGridV2Integration
    {
        static PastoralGridV2Integration()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += State;
        }

        static void State(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode &&
                SessionState.GetBool("PastoralGridV2Testing", false))
                new GameObject("Temporary flat forest acceptance")
                    .AddComponent<PastoralGridV2Acceptance>()
                    .outputDirectory = SessionState.GetString("PastoralGridV2Output", "");
        }

        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                !SessionState.GetBool("PastoralGridV2Testing", false))
                return;
            string output = SessionState.GetString("PastoralGridV2Output", "");
            string report = Path.Combine(output, "unity_acceptance.json");
            if (File.Exists(report))
            {
                SessionState.SetBool("PastoralGridV2Testing", false);
                EditorApplication.isPlaying = false;
                var result = JsonUtility.FromJson<PastoralGridV2Acceptance.Result>(File.ReadAllText(report));
                Debug.Log("PASTORAL_GRID_V2_ACCEPTANCE_FINISHED " + result.passed + " " + output);
                if (Application.isBatchMode && SessionState.GetBool("PastoralGridV2Batch", false))
                {
                    SessionState.SetBool("PastoralGridV2Batch", false);
                    EditorApplication.delayCall += () => EditorApplication.Exit(result.passed ? 0 : 2);
                }
            }
            else if (DateTime.TryParse(SessionState.GetString("PastoralGridV2Deadline", ""),
                                       out var deadline) &&
                     DateTime.UtcNow > deadline.ToUniversalTime())
            {
                SessionState.SetBool("PastoralGridV2Testing", false);
                Debug.LogError("PASTORAL_GRID_V2_TIMEOUT");
                if (Application.isBatchMode)
                    EditorApplication.Exit(3);
                else
                    EditorApplication.isPlaying = false;
            }
        }
    }
}
