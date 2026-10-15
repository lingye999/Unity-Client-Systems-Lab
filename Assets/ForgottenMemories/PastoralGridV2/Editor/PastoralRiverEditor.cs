using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ForgottenMemories.PastoralGridV2.Editor
{
    public static class PastoralRiverEditor
    {
        public const string ScenePath = PastoralGridV2Editor.Root + "/Scenes/RiverWoodlandV2.unity";

        static void Saved()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("请先保存当前场景，再打开河岸森林。");
        }

        public static void Open()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Stop Play Mode first.");
            Saved();
            PastoralGridV2Editor.Configure();
            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                return;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("River woodland camera");
            go.tag = "MainCamera";
            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.13f, .19f, .15f);
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.cullingMask = 1 << 8;
            camera.transparencySortMode = TransparencySortMode.Orthographic;
            var r = new GameObject("River Woodland - variable widths and grass walks")
                        .AddComponent<PastoralGridV2Runtime>();
            r.mapCamera = camera;
            r.seed = 202;
            r.style = 3;
            r.withRiver = true;
            r.Regenerate();
            PastoralGridV2Editor.Bake(r);
            Directory.CreateDirectory(PastoralGridV2Editor.Root + "/Scenes");
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("River woodland save failed");
            Selection.activeGameObject = r.gameObject;
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.in2DMode = true;
                SceneView.lastActiveSceneView.LookAt(new Vector3(24, -18, 0), Quaternion.identity, 20, true,
                                                     true);
            }
            Debug.Log("PASTORAL_RIVER_READY " + ScenePath);
        }

        [MenuItem("Forgotten Memories/Pastoral Grid V2/Run river acceptance")]
        public static void Acceptance()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Stop Play Mode first.");
            Open();
            string output = PastoralGridV2Editor.AcceptanceOutput("Water");
            Directory.CreateDirectory(output);
            SessionState.SetString("PastoralRiverOutput", output);
            SessionState.SetString("PastoralRiverDeadline", DateTime.UtcNow.AddMinutes(8).ToString("O"));
            SessionState.SetBool("PastoralRiverTesting", true);
            EditorApplication.isPlaying = true;
        }

        public static void BatchVerify()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Requires batch mode");
            try
            {
                // 测试参数由 Play Mode 验收组件设置，运行前不修改或保存当前场景。
                SessionState.SetBool("PastoralRiverBatch", true);
                Acceptance();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(2);
            }
        }
    }

    [InitializeOnLoad]
    public static class PastoralRiverIntegration
    {
        static PastoralRiverIntegration()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += State;
        }

        static void State(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode &&
                SessionState.GetBool("PastoralRiverTesting", false))
                new GameObject("Temporary river acceptance")
                    .AddComponent<PastoralRiverAcceptance>()
                    .outputDirectory = SessionState.GetString("PastoralRiverOutput", "");
        }

        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                !SessionState.GetBool("PastoralRiverTesting", false))
                return;
            string output = SessionState.GetString("PastoralRiverOutput", ""),
                   path = Path.Combine(output, "river_unity_acceptance.json");
            if (File.Exists(path))
            {
                var result = JsonUtility.FromJson<PastoralRiverAcceptance.Result>(File.ReadAllText(path));
                SessionState.SetBool("PastoralRiverTesting", false);
                EditorApplication.isPlaying = false;
                Debug.Log("PASTORAL_RIVER_ACCEPTANCE_FINISHED " + result.passed + " " + output);
                if (Application.isBatchMode && SessionState.GetBool("PastoralRiverBatch", false))
                {
                    SessionState.SetBool("PastoralRiverBatch", false);
                    EditorApplication.delayCall += () => EditorApplication.Exit(result.passed ? 0 : 2);
                }
            }
            else if (DateTime.TryParse(SessionState.GetString("PastoralRiverDeadline", ""),
                                       out var deadline) &&
                     DateTime.UtcNow > deadline.ToUniversalTime())
            {
                SessionState.SetBool("PastoralRiverTesting", false);
                Debug.LogError("PASTORAL_RIVER_TIMEOUT");
                if (Application.isBatchMode)
                    EditorApplication.Exit(3);
                else
                    EditorApplication.isPlaying = false;
            }
        }
    }
}
