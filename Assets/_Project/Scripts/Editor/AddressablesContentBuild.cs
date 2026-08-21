using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Company.ChestGame.Editor
{
    // Shipped tooling, not a throwaway setup script: a content build is something the project needs
    // on every release, because Minigame.Chests loads from a remote path and a player build no
    // longer carries it.
    //
    // Editor-only assembly, so nothing here can be referenced from game code by accident.
    public static class AddressablesContentBuild
    {
        // Entry point for ci/build-addressables.sh. Exits the editor itself rather than letting
        // batch mode decide, because -executeMethod reports a thrown exception and a clean return
        // with the same code otherwise.
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

        // Returns the build's own error string, empty when it succeeded, so both callers decide
        // what to do about it rather than this deciding for them.
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
