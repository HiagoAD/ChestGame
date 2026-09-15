using System.Collections;
using Company.ChestGame.Saving.Demo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.PlayMode
{
    /// <summary>
    /// Proves the authored SaveInspector prefab, its .uxml and its .uss are wired to
    /// <see cref="SaveInspectorPanel"/>, not just the panel's own logic against fakes.
    /// </summary>
    /// <remarks>
    /// See docs/testing.md, "What lives where".
    /// </remarks>
    public class SaveInspectorPanelPlayModeTests
    {
        private const string PrefabPath = "Assets/_Project/UI/SaveInspector/SaveInspector.prefab";

        /// <summary>
        /// Index of the in-memory option in the storage segmented control. This suite must never
        /// select the default File storage, which would touch a real save on disk.
        /// </summary>
        /// <remarks>
        /// See docs/testing.md, "The save suites never touch a real save", and docs/saving.md,
        /// "SaveBenchmark, and the number the plan got wrong".
        /// </remarks>
        private const int InMemoryStorageIndex = 3;

        private GameObject _instance;
        private SaveInspectorPanel _panel;

        [TearDown]
        public void TearDown()
        {
            if (_instance != null) Object.Destroy(_instance);
        }

        private static GameObject LoadPrefab()
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
#else
            return null;
#endif
        }

        /// <remarks>
        /// See docs/testing.md, "The save fixtures".
        /// </remarks>
        private IEnumerator BuildPanel()
        {
            GameObject prefab = LoadPrefab();
            Assert.IsNotNull(prefab, $"no save inspector prefab at {PrefabPath} - the panel cannot be tested without the asset it binds to");

            _instance = Object.Instantiate(prefab);
            _panel = _instance.GetComponent<SaveInspectorPanel>();
            Assert.IsNotNull(_panel, "the prefab has no SaveInspectorPanel on its root");

            yield return null;
            yield return null;
        }

        private VisualElement Root() => DocumentRoot("Chrome");
        private VisualElement ToggleRoot() => DocumentRoot("Toggle");
        private VisualElement Chrome() => Root().Q<VisualElement>("chrome");
        private Button Toggle() => ToggleRoot().Q<Button>("toggle-button");

        private VisualElement DocumentRoot(string child)
        {
            Transform found = _instance.transform.Find(child);
            Assert.IsNotNull(found, $"the prefab has no '{child}' child carrying a UIDocument");
            return found.GetComponent<UIDocument>().rootVisualElement;
        }
        private Button CloseButton() => Root().Q<Button>("close-button");

        private IEnumerator Expand()
        {
            Click(Toggle());
            yield return null;
        }

        [UnityTest]
        public IEnumerator Build_StartsCollapsed_ChromeHiddenAndToggleVisible()
        {
            yield return BuildPanel();

            Assert.IsNotNull(Toggle(), "no toggle button in the chrome - collapsed, this is the only way back in");
            Assert.Greater(Toggle().resolvedStyle.height, 0f,
                "the toggle itself has to be visible even though everything else is collapsed");

            Assert.AreEqual(DisplayStyle.None, Chrome().resolvedStyle.display,
                "the chrome should start collapsed");
        }

        [UnityTest]
        public IEnumerator TogglingExpanded_ShowsChrome_AndClosingHidesItAgain()
        {
            yield return BuildPanel();
            yield return Expand();

            Assert.AreEqual(DisplayStyle.Flex, Chrome().resolvedStyle.display,
                "expanding has to actually show the chrome, not just flip a field nothing reads");

            Click(CloseButton());
            yield return null;

            Assert.AreEqual(DisplayStyle.None, Chrome().resolvedStyle.display, "closing has to hide the chrome");
        }

        [UnityTest]
        public IEnumerator ExpandedOverlay_FillsThePanel_RatherThanCollapsingToTheHeightOfItsContents()
        {
            yield return BuildPanel();
            yield return Expand();

            Rect panel = Root().panel.visualTree.worldBound;
            Assert.Greater(panel.height, 0f, "guard: the panel itself never got a size");

            Assert.AreEqual(panel.height, Chrome().worldBound.height, 0.5f,
                "the chrome does not fill the panel - .chrome is absolute against all four edges precisely so it does not size to its contents");
        }

        /// <remarks>
        /// See docs/testing.md, "The save fixtures".
        /// </remarks>
        [UnityTest]
        public IEnumerator ExpandedControls_AreInsideTheChrome_AndResolveToATouchFriendlyHeight()
        {
            yield return BuildPanel();
            yield return Expand();

            Button save = Root().Q<Button>("save-button");
            Label readout = Root().Q<Label>("readout-label");
            Label bytes = Root().Q<Label>("bytes-label");

            Assert.IsNotNull(save, "no save-button in the chrome - SaveInspector.uxml and the panel disagree about its name");
            Assert.IsNotNull(readout, "no readout-label in the chrome");
            Assert.IsNotNull(bytes, "no bytes-label in the chrome");

            Assert.Greater(save.resolvedStyle.height, 80f,
                "the Save button resolved under a usable touch target, so SaveInspector.uss is not being applied");

            Rect chrome = Chrome().worldBound;
            AssertInside(chrome, save.worldBound, "the Save button");
            AssertInside(chrome, readout.worldBound, "the readout label");
            AssertInside(chrome, bytes.worldBound, "the stored-bytes label");
        }

        [UnityTest]
        public IEnumerator EitherWayRound_ExactlyOneToggleIsOnScreen_AndThePanelCanActuallyHitIt()
        {
            yield return BuildPanel();

            Button toggle = Toggle();
            Assert.AreEqual(toggle, toggle.panel.Pick(toggle.worldBound.center),
                "collapsed, a tap on the floating toggle does not land on it - it is the only way into the demo");

            yield return Expand();

            Assert.AreEqual(DisplayStyle.None, toggle.resolvedStyle.display,
                "expanded, the floating toggle has to be out of the way");

            Button close = CloseButton();
            Assert.AreEqual(close, Root().panel.Pick(close.worldBound.center),
                "expanded, a tap on Close does not land on it - the demo would open and never close again");
        }

        /// <remarks>
        /// See docs/testing.md, "The save fixtures".
        /// </remarks>
        [UnityTest]
        public IEnumerator ControlRows_KeepEveryControlOnScreen_AtTheNarrowestWidthAPhoneGives()
        {
            yield return BuildPanel();
            yield return Expand();

            const float narrowestPanelWidth = 1920f * 9f / 20f;
            const float chromePadding = 24f;

            VisualElement chrome = Chrome();
            chrome.style.right = StyleKeyword.Auto;
            chrome.style.width = narrowestPanelWidth;
            yield return null;

            Assert.AreEqual(narrowestPanelWidth, chrome.worldBound.width, 1f,
                "guard: the chrome did not take the width being tested");

            float rightmostAllowed = narrowestPanelWidth - chromePadding;

            foreach (VisualElement row in new[] { chrome[1], chrome[2], chrome[3], chrome[4] })
            {
                foreach (VisualElement control in row.Children())
                {
                    Assert.LessOrEqual(control.worldBound.xMax, rightmostAllowed + 0.5f,
                        $"a control runs to {control.worldBound.xMax}px on a 9:20 phone, past the {rightmostAllowed}px of usable width");
                }
            }
        }

        /// <remarks>
        /// See docs/saving.md, "The tamper button, and why it edits two different ways".
        /// </remarks>
        [UnityTest]
        public IEnumerator ClickingSave_ThenTamper_OverInMemory_SettlesWithARealAcceptedOutcome()
        {
            yield return BuildPanel();
            yield return Expand();

            VisualElement root = Root();
            Label readout = root.Q<Label>("readout-label");
            Label timings = root.Q<Label>("timings-label");
            Label tamperLabel = root.Q<Label>("tamper-label");
            VisualElement tamperReadout = root.Q<VisualElement>("tamper-readout");
            Button tamperButton = root.Q<Button>("tamper-button");
            string idleReadout = readout.text;
            string idleTamper = tamperLabel.text;

            Click(root.Q<Button>($"storage-{InMemoryStorageIndex}"));
            Click(root.Q<Button>("save-button"));

            const int settleFrames = 20;
            for (int frame = 0; frame < settleFrames && readout.text == idleReadout; frame++) yield return null;

            Assert.That(readout.text, Is.Not.EqualTo(idleReadout),
                "the readout is still showing its pre-click idle text even though Save was clicked");
            Assert.That(timings.text, Does.Contain("ms"), "no timing was recorded for the save");
            Assert.IsTrue(tamperButton.enabledSelf, "Tamper should unlock once a save has actually landed");

            Click(tamperButton);
            for (int frame = 0; frame < settleFrames && tamperLabel.text == idleTamper; frame++) yield return null;

            Assert.That(tamperLabel.text, Is.Not.EqualTo(idleTamper),
                "the tamper readout is still showing its pre-click idle text even though Tamper was clicked");
            Assert.IsTrue(tamperReadout.ClassListContains("tamper-readout--accepted"),
                "None over InMemory decodes and re-encodes rather than catching the edit - this combination should read as accepted");
        }

        private static void AssertInside(Rect outer, Rect inner, string what)
        {
            const float tolerance = 0.5f;

            Assert.GreaterOrEqual(inner.xMin, outer.xMin - tolerance, $"{what} starts left of the chrome that should contain it");
            Assert.LessOrEqual(inner.xMax, outer.xMax + tolerance, $"{what} runs past the chrome's right edge");
            Assert.GreaterOrEqual(inner.yMin, outer.yMin - tolerance, $"{what} starts above the chrome that should contain it");
            Assert.LessOrEqual(inner.yMax, outer.yMax + tolerance, $"{what} runs past the chrome's bottom edge");
        }

        /// <remarks>
        /// See docs/testing.md, "Simulating a click in a PlayMode UI test".
        /// </remarks>
        private static void Click(Button button)
        {
            Assert.IsNotNull(button, "guard: cannot click a button the query did not find");

            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }
    }
}
