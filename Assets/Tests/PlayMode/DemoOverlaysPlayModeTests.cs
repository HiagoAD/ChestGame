using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.PlayMode
{
    // Both demo overlays live in the Game scene at once, each with a floating toggle. Collapsed,
    // those two toggles have to sit apart, or one hides the other and that demo cannot be opened.
    public class DemoOverlaysPlayModeTests
    {
        private const string PoolingDemoPrefabPath = "Assets/_Project/UI/PoolingDemo/PoolingDemo.prefab";
        private const string SaveInspectorPrefabPath = "Assets/_Project/UI/SaveInspector/SaveInspector.prefab";

        private GameObject _pooling;
        private GameObject _save;

        [TearDown]
        public void TearDown()
        {
            if (_pooling != null) Object.Destroy(_pooling);
            if (_save != null) Object.Destroy(_save);
        }

        private static GameObject Load(string path)
        {
#if UNITY_EDITOR
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"no prefab at {path}");
            return prefab;
#else
            return null;
#endif
        }

        [UnityTest]
        public IEnumerator Collapsed_TheTwoDemoToggles_DoNotOverlap()
        {
            _pooling = Object.Instantiate(Load(PoolingDemoPrefabPath));
            _save = Object.Instantiate(Load(SaveInspectorPrefabPath));

            // Both bind in Start and resolve layout on their panel's own update.
            yield return null;
            yield return null;

            Button poolingToggle = _pooling.GetComponent<UIDocument>().rootVisualElement.Q<Button>("toggle-button");
            Button saveToggle = _save.transform.Find("Toggle").GetComponent<UIDocument>().rootVisualElement.Q<Button>("toggle-button");
            Assert.IsNotNull(poolingToggle, "the pooling demo has no toggle-button");
            Assert.IsNotNull(saveToggle, "the save inspector has no toggle-button");

            // Comparable because both PanelSettings scale to the same reference resolution the
            // same way, so a panel pixel means the same screen area in each.
            Rect pooling = poolingToggle.worldBound;
            Rect save = saveToggle.worldBound;
            Assert.Greater(pooling.height, 0f, "guard: the pooling toggle has not been laid out");
            Assert.Greater(save.height, 0f, "guard: the save inspector toggle has not been laid out");

            Assert.IsFalse(pooling.Overlaps(save),
                $"the two floating toggles overlap (pooling {pooling}, save inspector {save}), so one hides the other");
        }

        [UnityTest]
        public IEnumerator Collapsed_TheTwoDemoToggles_FormOneAlignedColumn()
        {
            _pooling = Object.Instantiate(Load(PoolingDemoPrefabPath));
            _save = Object.Instantiate(Load(SaveInspectorPrefabPath));

            yield return null;
            yield return null;

            Rect pooling = _pooling.GetComponent<UIDocument>().rootVisualElement.Q<Button>("toggle-button").worldBound;
            Rect save = _save.transform.Find("Toggle").GetComponent<UIDocument>().rootVisualElement.Q<Button>("toggle-button").worldBound;

            // Each toggle is as wide as its label needs, above a shared floor. A label that outgrows
            // the floor widens only its own button and leaves a ragged edge on the stack, which is
            // what this catches.
            Assert.AreEqual(pooling.width, save.width, 0.5f,
                $"the toggles differ in width (pooling {pooling.width}, save inspector {save.width}) - a label has outgrown the shared minimum");
            Assert.AreEqual(pooling.xMin, save.xMin, 0.5f, "the toggles' left edges do not line up");
            Assert.AreEqual(pooling.xMax, save.xMax, 0.5f, "the toggles' right edges do not line up");
            Assert.AreEqual(pooling.height, save.height, 0.5f, "the toggles differ in height");
        }
    }
}
