using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ForgottenMemories.PastoralGridV2.Editor
{
    // 验证真实的 Inspector 配方路径；只创建默认配方资产，不保存或重烘焙现有场景。
    public static class ForestArchitectureAudit
    {
        [Serializable]
        public sealed class Result
        {
            public bool passed;
            public string error;
            public string profilePath;
            public bool profileCreated;
            public bool defaultProfileMatches;
            public bool editedProfileChangesLayout;
            public bool invalidProfilePreservesMap;
            public int physicsPositions;
            public int physicsSweeps;
        }

        public static void Run()
        {
            if (!Application.isBatchMode)
            {
                throw new InvalidOperationException("Architecture audit requires batch mode.");
            }

            string output = null;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-forestArchitectureOutput")
                {
                    output = args[i + 1];
                }
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new ArgumentException("Missing -forestArchitectureOutput");
            }

            var result =
                new Result { profilePath = PastoralGridV2Editor.Root + "/Profiles/ForestDefault.asset" };
            GameObject root = null;
            ForestGenerationProfile temporaryProfile = null;
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                string folder = PastoralGridV2Editor.Root + "/Profiles";
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    AssetDatabase.CreateFolder(PastoralGridV2Editor.Root, "Profiles");
                }

                var profile = AssetDatabase.LoadAssetAtPath<ForestGenerationProfile>(result.profilePath);
                if (profile == null)
                {
                    if (File.Exists(result.profilePath))
                    {
                        throw new InvalidOperationException("Profile path is occupied by another asset.");
                    }
                    profile = ScriptableObject.CreateInstance<ForestGenerationProfile>();
                    AssetDatabase.CreateAsset(profile, result.profilePath);
                    AssetDatabase.SaveAssets();
                    result.profileCreated = true;
                }

                root = new GameObject("Forest architecture probe");
                var runtime = root.AddComponent<PastoralGridV2Runtime>();
                runtime.seed = 202;
                runtime.style = 3;
                runtime.withRiver = true;
                runtime.showControls = false;
                runtime.Regenerate();
                string baseline = JsonUtility.ToJson(runtime.GridLayout);

                // 使用非持久副本测试编辑行为，避免修改用户以后调过的配方。
                temporaryProfile = Object.Instantiate(profile);
                temporaryProfile.recipe = new ForestGenerationRecipe();
                runtime.generationProfile = temporaryProfile;
                runtime.Regenerate();
                result.defaultProfileMatches = baseline == JsonUtility.ToJson(runtime.GridLayout);
                Require(result.defaultProfileMatches, "Default profile changed the existing map.");
                CheckPhysics(runtime, result);

                temporaryProfile.recipe.vegetation.baseDensity = .40;
                runtime.Regenerate();
                result.editedProfileChangesLayout = baseline != JsonUtility.ToJson(runtime.GridLayout);
                Require(result.editedProfileChangesLayout, "Profile edit did not reach generation.");
                CheckPhysics(runtime, result);

                var previousObjects = runtime.Generated;
                var previousLayout = runtime.GridLayout;
                var previousStatistics = runtime.PlacementStatistics;
                int previousAssets = runtime.NewAssets.Count;
                temporaryProfile.recipe.placement.rootRoadPaddingPixels = 0;
                bool rejected = false;
                try
                {
                    runtime.Regenerate();
                }
                catch (ArgumentException)
                {
                    rejected = true;
                }
                result.invalidProfilePreservesMap = rejected && runtime.Generated == previousObjects &&
                                                    runtime.GridLayout == previousLayout &&
                                                    runtime.PlacementStatistics == previousStatistics &&
                                                    runtime.NewAssets.Count == previousAssets &&
                                                    previousObjects.activeSelf;
                Require(result.invalidProfilePreservesMap, "Invalid profile replaced the usable map.");
                result.passed = true;
            }
            catch (Exception e)
            {
                result.error = e.ToString();
                Debug.LogException(e);
            }
            finally
            {
                if (root != null)
                {
                    Object.DestroyImmediate(root);
                }
                if (temporaryProfile != null)
                {
                    Object.DestroyImmediate(temporaryProfile);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(result, true));
            EditorApplication.Exit(result.passed ? 0 : 2);
        }

        private static void CheckPhysics(PastoralGridV2Runtime runtime, Result result)
        {
            var record = new PastoralMaintenanceAudit.Case();
            PastoralMaintenanceAudit.PhysicsCheck(runtime, record);
            result.physicsPositions += record.physicsPositions;
            result.physicsSweeps += record.physicsSweeps;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
