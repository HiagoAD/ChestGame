using Company.ChestGame.Saving.Demo;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Company.ChestGame.Editor
{
    // Builds Assets/_Project/UI/SaveInspector/SaveInspector.prefab from the authored .uxml and
    // PanelSettings: a committed, reproducible asset rather than hand-written prefab YAML. Safe to
    // re-run - this prefab is not a historical artefact, only a build of the two source assets.
    public static class SaveInspectorPrefabGenerator
    {
        private const string UxmlPath = "Assets/_Project/UI/SaveInspector/SaveInspector.uxml";
        private const string PanelSettingsPath = "Assets/_Project/UI/SaveInspector/SaveInspectorPanelSettings.asset";
        private const string PrefabPath = "Assets/_Project/UI/SaveInspector/SaveInspector.prefab";

        [MenuItem("Tools/Saving/Generate Save Inspector Prefab")]
        public static void Generate()
        {
            VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (uxml == null)
            {
                Debug.LogError($"No VisualTreeAsset at {UxmlPath}. Nothing was written.");
                return;
            }

            PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings == null)
            {
                Debug.LogError($"No PanelSettings at {PanelSettingsPath}. Nothing was written.");
                return;
            }

            GameObject root = new("SaveInspector");
            try
            {
                UIDocument document = root.AddComponent<UIDocument>();
                document.panelSettings = settings;
                document.visualTreeAsset = uxml;

                root.AddComponent<SaveInspectorPanel>();

                SerializedObject serializedPanel = new(root.GetComponent<SaveInspectorPanel>());
                serializedPanel.FindProperty("_document").objectReferenceValue = document;
                serializedPanel.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log($"Wrote {PrefabPath}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
