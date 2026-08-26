using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Company.ChestGame.Editor
{
    // Shipped tooling: Minigame.Chests loads from a remote path, so a content build is needed on
    // every release. Editor-only assembly, so game code cannot reference it by accident.
    public static class AddressablesContentBuild
    {
        // Entry point for ci/build-addressables.sh. Exits the editor itself, because -executeMethod
        // otherwise reports a thrown exception and a clean return with the same code.
        public static void BuildFromCommandLine()
        {
            try
            {
                string error = Build();

                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogError($"Addressables content build failed: {error}");
                    EditorApplication.Exit(1);
                    return;
                }

                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Addressables content build threw: {exception}");
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("Build/Addressables Content")]
        public static void BuildFromMenu()
        {
            string error = Build();

            if (string.IsNullOrEmpty(error))
            {
                Debug.Log("Addressables content build finished.");
                return;
            }

            Debug.LogError($"Addressables content build failed: {error}");
        }

        // The build's own error string, empty when it succeeded, so both callers decide.
        private static string Build()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                return "the project has no AddressableAssetSettings; open the Addressables Groups window once";
            }

            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

            return result?.Error ?? string.Empty;
        }
    }
}
