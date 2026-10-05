using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Company.ChestGame.Editor
{
    /// <summary>
    /// Shipped tooling that runs the Addressables content build.
    /// </summary>
    /// <remarks>
    /// See docs/content-delivery.md, "Building and serving".
    /// </remarks>
    public static class AddressablesContentBuild
    {
        /// <summary>
        /// Entry point for <c>ci/build-addressables.sh</c>.
        /// </summary>
        /// <remarks>
        /// See docs/content-delivery.md, "Building and serving".
        /// </remarks>
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

        /// <returns>The build's own error string, empty when it succeeded, so both callers decide.</returns>
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
