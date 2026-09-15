using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.PlayMode
{
    /// <summary>
    /// Instantiates both demo overlay prefabs and asserts their collapsed floating toggles against
    /// each other.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "Two overlays in one scene, and why the save inspector uses two documents".
    /// </remarks>
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

        /// <remarks>
        /// See docs/saving.md, "Two overlays in one scene, and why the save inspector uses two documents".
        /// </remarks>
        [UnityTest]
        public IEnumerator Collapsed_TheTwoDemoToggles_DoNotOverlap()
        {
            _pooling = Object.Instantiate(Load(PoolingDemoPrefabPath));
            _save = Object.Instantiate(Load(SaveInspectorPrefabPath));

            yield return null;
            yield return null;

            Button poolingToggle = _pooling.GetComponent<UIDocument>().rootVisualElement.Q<Button>("toggle-button");
            Button saveToggle = _save.transform.Find("Toggle").GetComponent<UIDocument>().rootVisualElement.Q<Button>("toggle-button");
            Assert.IsNotNull(poolingToggle, "the pooling demo has no toggle-button");
            Assert.IsNotNull(saveToggle, "the save inspector has no toggle-button");

            Rect pooling = poolingToggle.worldBound;
            Rect save = saveToggle.worldBound;
            Assert.Greater(pooling.height, 0f, "guard: the pooling toggle has not been laid out");
            Assert.Greater(save.height, 0f, "guard: the save inspector toggle has not been laid out");

            Assert.IsFalse(pooling.Overlaps(save),
                $"the two floating toggles overlap (pooling {pooling}, save inspector {save}), so one hides the other");
        }

        /// <remarks>
        /// See docs/saving.md, "Two overlays in one scene, and why the save inspector uses two documents".
        /// </remarks>
        [UnityTest]
        public IEnumerator Collapsed_TheTwoDemoToggles_FormOneAlignedColumn()
        {
            _pooling = Object.Instantiate(Load(PoolingDemoPrefabPath));
            _save = Object.Instantiate(Load(SaveInspectorPrefabPath));

            yield return null;
            yield return null;

            Rect pooling = _pooling.GetComponent<UIDocument>().rootVisualElement.Q<Button>("toggle-button").worldBound;
            Rect save = _save.transform.Find("Toggle").GetComponent<UIDocument>().rootVisualElement.Q<Button>("toggle-button").worldBound;

            Assert.AreEqual(pooling.width, save.width, 0.5f,
                $"the toggles differ in width (pooling {pooling.width}, save inspector {save.width}) - a label has outgrown the shared minimum");
            Assert.AreEqual(pooling.xMin, save.xMin, 0.5f, "the toggles' left edges do not line up");
            Assert.AreEqual(pooling.xMax, save.xMax, 0.5f, "the toggles' right edges do not line up");
            Assert.AreEqual(pooling.height, save.height, 0.5f, "the toggles differ in height");
        }
    }
}
