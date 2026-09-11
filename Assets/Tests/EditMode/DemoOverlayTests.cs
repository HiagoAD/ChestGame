using NUnit.Framework;
using UnityEngine.UIElements;

namespace Company.ChestGame.Tests.EditMode
{
    // The two demo overlays share the Game scene, and each opens full screen over everything
    // including the other one's floating toggle. Run against the authored assets rather than copies
    // of them, since every way this breaks is a scene or asset edit that compiles cleanly.
    public class DemoOverlayTests
    {
        private const string GameScenePath = "Assets/_Project/Scenes/Game.unity";
        private const string PoolingDemoPrefabPath = "Assets/_Project/UI/PoolingDemo/PoolingDemo.prefab";
        private const string SaveInspectorPrefabPath = "Assets/_Project/UI/SaveInspector/SaveInspector.prefab";

        private const string PoolingSettingsPath = "Assets/_Project/UI/PoolingDemo/PoolingDemoPanelSettings.asset";
        private const string SaveChromeSettingsPath = "Assets/_Project/UI/SaveInspector/SaveInspectorPanelSettings.asset";
        private const string SaveToggleSettingsPath = "Assets/_Project/UI/SaveInspector/SaveInspectorTogglePanelSettings.asset";

        [TestCase(PoolingDemoPrefabPath)]
        [TestCase(SaveInspectorPrefabPath)]
        public void TheGameScene_PlacesTheDemoOverlay(string prefabPath)
        {
#if UNITY_EDITOR
            string[] dependencies = UnityEditor.AssetDatabase.GetDependencies(GameScenePath, recursive: false);

            CollectionAssert.Contains(dependencies, prefabPath,
                $"{GameScenePath} no longer places {prefabPath}, so the demo cannot be opened from the running game");
#else
            Assert.Ignore("Reads an authored scene through the AssetDatabase, so it only runs in the editor.");
#endif
        }

        [Test]
        public void TheSaveInspector_SortsItsToggleBelow_AndItsChromeAbove_ThePoolingDemo()
        {
#if UNITY_EDITOR
            float pooling = Load(PoolingSettingsPath).sortingOrder;
            float saveToggle = Load(SaveToggleSettingsPath).sortingOrder;
            float saveChrome = Load(SaveChromeSettingsPath).sortingOrder;

            // Toggle below: an open pooling demo covers it. Chrome above: an open save inspector
            // covers the pooling demo's toggle. Either inequality failing leaves one overlay's
            // toggle floating on top of the other's open panel.
            Assert.Less(saveToggle, pooling, "the save inspector's toggle would float over an open pooling demo");
            Assert.Greater(saveChrome, pooling, "the pooling demo's toggle would float over an open save inspector");
#else
            Assert.Ignore("Reads authored PanelSettings through the AssetDatabase, so it only runs in the editor.");
#endif
        }

#if UNITY_EDITOR
        private static PanelSettings Load(string path)
        {
            PanelSettings settings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            Assert.IsNotNull(settings, $"no PanelSettings at {path}");
            return settings;
        }
#endif
    }
}
