using Company.ChestGame.Saving.Demo;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Company.ChestGame.Editor
{
    /// <summary>
    /// Builds Assets/_Project/UI/SaveInspector/SaveInspector.prefab from the authored .uxml files and
    /// PanelSettings. Safe to re-run: an existing prefab is updated in place rather than replaced.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "Two overlays in one scene, and why the save inspector uses two documents".
    /// </remarks>
    public static class SaveInspectorPrefabGenerator
    {
        private const string UxmlPath = "Assets/_Project/UI/SaveInspector/SaveInspector.uxml";
        private const string ToggleUxmlPath = "Assets/_Project/UI/SaveInspector/SaveInspectorToggle.uxml";
        private const string PanelSettingsPath = "Assets/_Project/UI/SaveInspector/SaveInspectorPanelSettings.asset";
        private const string TogglePanelSettingsPath = "Assets/_Project/UI/SaveInspector/SaveInspectorTogglePanelSettings.asset";
        private const string PrefabPath = "Assets/_Project/UI/SaveInspector/SaveInspector.prefab";

        private const string RootName = "SaveInspector";
        private const string ChromeChildName = "Chrome";
        private const string ToggleChildName = "Toggle";

        /// <remarks>
        /// See docs/saving.md, "Two overlays in one scene, and why the save inspector uses two documents".
        /// </remarks>
        [MenuItem("Tools/Saving/Generate Save Inspector Prefab")]
        public static void Generate()
        {
            VisualTreeAsset uxml = Require<VisualTreeAsset>(UxmlPath);
            VisualTreeAsset toggleUxml = Require<VisualTreeAsset>(ToggleUxmlPath);
            PanelSettings settings = Require<PanelSettings>(PanelSettingsPath);
            PanelSettings toggleSettings = Require<PanelSettings>(TogglePanelSettingsPath);
            if (uxml == null || toggleUxml == null || settings == null || toggleSettings == null) return;

            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            GameObject root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject(RootName);
            try
            {
                if (root.TryGetComponent(out UIDocument onRoot)) Object.DestroyImmediate(onRoot);

                UIDocument document = GetOrAdd<UIDocument>(GetOrAddChild(root, ChromeChildName));
                document.panelSettings = settings;
                document.visualTreeAsset = uxml;

                UIDocument toggleDocument = GetOrAdd<UIDocument>(GetOrAddChild(root, ToggleChildName));
                toggleDocument.panelSettings = toggleSettings;
                toggleDocument.visualTreeAsset = toggleUxml;

                SerializedObject serializedPanel = new(GetOrAdd<SaveInspectorPanel>(root));
                serializedPanel.FindProperty("_document").objectReferenceValue = document;
                serializedPanel.FindProperty("_toggleDocument").objectReferenceValue = toggleDocument;
                serializedPanel.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"Wrote {PrefabPath}");
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
        }

        private static T Require<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) Debug.LogError($"No {typeof(T).Name} at {path}. Nothing was written.");
            return asset;
        }

        private static GameObject GetOrAddChild(GameObject parent, string name)
        {
            Transform child = parent.transform.Find(name);
            if (child != null) return child.gameObject;

            GameObject created = new(name);
            created.transform.SetParent(parent.transform, false);
            return created;
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component =>
            target.TryGetComponent(out T existing) ? existing : target.AddComponent<T>();
    }
}
