using System.Collections;
using Company.ChestGame.Pooling.Demo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.PlayMode
{
    // PoolRacePlayModeTests proves the lanes still work as real Unity objects; this proves the
    // authored chrome around them is wired to the panel that drives it. That risk is specific to a
    // UI built from assets rather than from code: a renamed element in PoolingDemo.uxml, a missing
    // class in PoolingDemo.uss, or a serialized field left empty on the prefab all compile perfectly
    // and produce a panel that does nothing.
    public class PoolingDemoPanelPlayModeTests
    {
        private const string PrefabPath = "Assets/_Project/UI/PoolingDemo/PoolingDemo.prefab";

        private GameObject _instance;
        private PoolingDemoPanel _panel;

        [TearDown]
        public void TearDown()
        {
            if (_instance != null) Object.Destroy(_instance);
        }

        // Loaded through the AssetDatabase rather than Resources or Addressables: this suite only
        // runs inside the editor, and the alternatives each mean shipping the demo prefab somewhere
        // the game does not need it just so a test can reach it.
        private static GameObject LoadPrefab()
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
#else
            return null;
#endif
        }

        private IEnumerator BuildPanel()
        {
            GameObject prefab = LoadPrefab();
            Assert.IsNotNull(prefab, $"no demo prefab at {PrefabPath} - the panel cannot be tested without the asset it binds to");

            _instance = Object.Instantiate(prefab);
            _panel = _instance.GetComponent<PoolingDemoPanel>();
            Assert.IsNotNull(_panel, "the demo prefab has no PoolingDemoPanel on its root");

            // The panel binds in Start and Yoga resolves layout on the panel's own update, so
            // nothing below can be read on the frame the instance was created.
            yield return null;
            yield return null;
        }

        private VisualElement Root() => _panel.GetComponent<UIDocument>().rootVisualElement;
        private VisualElement Chrome() => Root().Q<VisualElement>("chrome");
        private Button Toggle() => Root().Q<Button>("toggle-button");
        private Button CloseButton() => Root().Q<Button>("close-button");

        // The panel's own extent, taken from the visual tree PanelSettings sizes to the screen
        // rather than from anything the demo builds. It has to come from outside the demo, or a
        // chrome collapsed to nothing would be measured against itself and report a perfect fit.
        private VisualElement PanelRoot() => Root().panel.visualTree;

        private IEnumerator Expand()
        {
            Click(Toggle());
            yield return null;
        }

        [UnityTest]
        public IEnumerator Build_StartsCollapsed_ChromeHiddenAndLaneHostDisabled()
        {
            yield return BuildPanel();

            Assert.IsNotNull(Toggle(), "no toggle button in the chrome - collapsed, this is the only way back in");
            Assert.Greater(Toggle().resolvedStyle.height, 0f,
                "the toggle itself has to be visible even though everything else is collapsed");

            Assert.AreEqual(DisplayStyle.None, Chrome().resolvedStyle.display,
                "the chrome should start collapsed - always-visible content is exactly the HUD overlap this panel avoids");

            Canvas lanesCanvas = FindLanesCanvas();
            Assert.IsFalse(lanesCanvas.enabled,
                "the lanes' host Canvas should start disabled - collapsed has to hide the uGUI lanes too, not just the UI Toolkit chrome");
        }

        [UnityTest]
        public IEnumerator TogglingExpanded_ShowsChromeAndLanes_AndClosingHidesBothAgain()
        {
            yield return BuildPanel();

            Canvas lanesCanvas = FindLanesCanvas();

            yield return Expand();

            Assert.AreEqual(DisplayStyle.Flex, Chrome().resolvedStyle.display,
                "expanding has to actually show the chrome, not just flip a field nothing reads");
            Assert.IsTrue(lanesCanvas.enabled,
                "expanding has to re-enable the lanes' host Canvas too - a chrome-only expand would show controls over empty lanes");

            // Through Close, not the floating toggle: expanded, the floating toggle is the one that
            // is hidden, and collapsing through it would test a control no player can reach.
            Click(CloseButton());
            yield return null;

            Assert.AreEqual(DisplayStyle.None, Chrome().resolvedStyle.display, "closing has to hide the chrome");
            Assert.IsFalse(lanesCanvas.enabled, "closing has to hide the lane host too");
        }

        [UnityTest]
        public IEnumerator ExpandedOverlay_FillsThePanel_RatherThanCollapsingToTheHeightOfItsContents()
        {
            yield return BuildPanel();
            yield return Expand();

            Rect panel = PanelRoot().worldBound;
            Assert.Greater(panel.height, 0f, "guard: the panel itself never got a size");

            // .chrome is absolute against all four edges for this reason: the document root holds
            // only absolutely positioned children, so nothing is in flow to size it and a chrome
            // that grew instead would collapse to its contents.
            Assert.AreEqual(panel.height, Chrome().worldBound.height, 0.5f,
                "the chrome does not fill the panel - it sized to its contents, which is a band across the top with the controls clipped out of it");
        }

        [UnityTest]
        public IEnumerator ExpandedControls_AreInsideTheChrome_AndResolveToATouchFriendlyHeight()
        {
            yield return BuildPanel();
            yield return Expand();

            Button run = Root().Q<Button>("run-button");
            Label readout = Root().Q<Label>("readout-label");
            Label metrics = Root().Q<Label>("lane-metrics-0");

            Assert.IsNotNull(run, "no run-button in the chrome - PoolingDemo.uxml and the panel disagree about its name");
            Assert.IsNotNull(readout, "no readout-label in the chrome");
            Assert.IsNotNull(metrics, "no lane-metrics-0 in the chrome");

            // A control carrying a min-height resolves to it whether or not the box around it can
            // show a single pixel, so height alone proves nothing - hence the containment checks.
            Assert.Greater(run.resolvedStyle.height, 80f,
                "the Run button resolved under a usable touch target, so PoolingDemo.uss is not being applied");

            Rect chrome = Chrome().worldBound;
            AssertInside(chrome, run.worldBound, "the Run button");
            AssertInside(chrome, readout.worldBound, "the readout label");
            AssertInside(chrome, metrics.worldBound, "the first lane's metrics label");
        }

        [UnityTest]
        public IEnumerator EitherWayRound_ExactlyOneToggleIsOnScreen_AndThePanelCanActuallyHitIt()
        {
            // Asked through the panel's own hit test rather than display flags, because the failure
            // this pins is invisible to flags: the toggle and the chrome are absolutely positioned
            // children of one root with no z-index between them, so the later child wins the pixel.
            // A toggle behind an opaque backdrop is display:Flex, has a real resolved size, and
            // answers a forced-target SendEvent, while being unhittable. Painting it last is not the
            // fix either - the toggle's band runs through a control row, so it would sit on that
            // row's last button.
            yield return BuildPanel();

            Button toggle = Toggle();
            Assert.AreEqual(toggle, Root().panel.Pick(toggle.worldBound.center),
                "collapsed, a tap on the floating toggle does not land on it - it is the only way into the demo");

            yield return Expand();

            Assert.AreEqual(DisplayStyle.None, toggle.resolvedStyle.display,
                "expanded, the floating toggle has to be out of the way - its band runs through a control row");

            Button close = CloseButton();
            Assert.AreEqual(close, Root().panel.Pick(close.worldBound.center),
                "expanded, a tap on Close does not land on it - the demo would open and never close again");
        }

        [UnityTest]
        public IEnumerator ControlRows_KeepEveryControlOnScreen_AtTheNarrowestWidthAPhoneGives()
        {
            yield return BuildPanel();
            yield return Expand();

            // Matching on height against a 1080x1920 reference makes the panel 1920 logical px tall
            // on every device and 1920*(w/h) wide, so the reference's own 1080 is the width of a
            // 9:16 phone, not a floor. A 9:20 phone gives 864, and the control rows do not wrap.
            const float narrowestPanelWidth = 1920f * 9f / 20f;
            const float chromePadding = 24f;

            // Pinned to that width directly, because the panel's width comes from the real screen and
            // a test cannot resize it. These controls shrink, so a right edge measured at whatever
            // width this run happens to have would not transfer to 864.
            VisualElement chrome = Chrome();
            chrome.style.right = StyleKeyword.Auto;
            chrome.style.width = narrowestPanelWidth;
            yield return null;

            Assert.AreEqual(narrowestPanelWidth, chrome.worldBound.width, 1f,
                "guard: the chrome did not take the width being tested");

            float rightmostAllowed = narrowestPanelWidth - chromePadding;

            // [0] is the title bar; the two control rows follow it.
            foreach (VisualElement row in new[] { chrome[1], chrome[2] })
            {
                foreach (VisualElement control in row.Children())
                {
                    Assert.LessOrEqual(control.worldBound.xMax, rightmostAllowed + 0.5f,
                        $"a control runs to {control.worldBound.xMax}px on a 9:20 phone, past the {rightmostAllowed}px of usable width - it would be off the screen edge and untappable");
                }
            }
        }

        [UnityTest]
        public IEnumerator ThePrefabsOwnScalerAndPanelSettings_AgreeAboutWhatAPixelIs()
        {
            yield return BuildPanel();

            // The chrome is UI Toolkit and the lanes are uGUI, laid out by two systems that only
            // agree if both scale the same way. Nothing else would notice them drifting: each looks
            // right on its own, and the chrome only starts covering the lanes on hardware whose
            // resolution does not match the reference. It is an authoring guarantee rather than
            // something reconciled at runtime, which is why it needs a test.
            CanvasScaler scaler = _instance.GetComponent<CanvasScaler>();
            PanelSettings settings = _panel.GetComponent<UIDocument>().panelSettings;

            Assert.IsNotNull(settings, "the prefab's UIDocument has no PanelSettings, so its chrome has no panel to render into");
            Assert.IsNotNull(settings.themeStyleSheet,
                "the PanelSettings has no theme style sheet - without one the controls have no font and render no text at all");

            Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
            Assert.AreEqual(PanelScaleMode.ScaleWithScreenSize, settings.scaleMode,
                "the chrome has to scale with the screen the way the demo's own Canvas does, or the two disagree about a pixel");
            Assert.AreEqual(new Vector2Int((int)scaler.referenceResolution.x, (int)scaler.referenceResolution.y), settings.referenceResolution,
                "and against the same reference resolution");
            Assert.AreEqual(PanelScreenMatchMode.MatchWidthOrHeight, settings.screenMatchMode, "and matching on the same axis");
            Assert.AreEqual(scaler.matchWidthOrHeight, settings.match, 0.0001f,
                "and by the same amount - a chrome matching on width drifts against a canvas matching on height on every other aspect ratio");
        }

        [UnityTest]
        public IEnumerator ClickingRun_ThroughTheElementsRealClickPath_StartsARaceAndSettlesWithRealResults()
        {
            yield return BuildPanel();

            // Reaching Run at all requires expanding first - collapsed, its whole row is
            // display:None. Same path a player is on, not a shortcut around it.
            yield return Expand();

            VisualElement root = Root();
            Label readout = root.Q<Label>("readout-label");
            Label metrics = root.Q<Label>("lane-metrics-0");
            string idleReadout = readout.text;

            // Smallest board on purpose: this only cares that a real click reaches the race.
            //
            // SendEvent does not run the handler inline - it enqueues, and the panel drains the
            // queue on its next update - so both clicks below land in that queue and are drained
            // together, in order, with nothing having run yet on this line.
            Click(root.Q<Button>("size-0"));
            Click(root.Q<Button>("run-button"));

            // No assertion on the transient "Running..." text: an 8-item board against a 2ms budget
            // can finish inside the same frame the queued clicks are drained in. Metrics only leave
            // "not run yet" from inside OnRaceCompleted, which only runs after a full race, which
            // only starts from OnRunClicked - so this one wait covers both "the click never reached
            // the handler" and "the handler ran but never started a race".
            const int settleFrames = 8 + 20;
            for (int frame = 0; frame < settleFrames && metrics.text == "not run yet"; frame++) yield return null;

            Assert.That(metrics.text, Does.StartWith("Placed"),
                "no lane reported a result within the settle window - the click did not start a race");
            Assert.That(readout.text, Is.Not.EqualTo(idleReadout),
                "the readout is still showing its pre-click idle text even though a lane reported a result");
        }

        private Canvas FindLanesCanvas()
        {
            foreach (Canvas canvas in _instance.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas.gameObject != _instance) return canvas;
            }

            Assert.Fail("the demo prefab has no nested Canvas for the lanes - there is nothing to switch off when it collapses");
            return null;
        }

        // Rect has Overlaps but no containment test, and overlapping is not the question: a chrome
        // clipping its own controls overlaps every one of them.
        private static void AssertInside(Rect outer, Rect inner, string what)
        {
            const float tolerance = 0.5f;

            Assert.GreaterOrEqual(inner.xMin, outer.xMin - tolerance, $"{what} starts left of the chrome that should contain it");
            Assert.LessOrEqual(inner.xMax, outer.xMax + tolerance, $"{what} runs past the chrome's right edge");
            Assert.GreaterOrEqual(inner.yMin, outer.yMin - tolerance, $"{what} starts above the chrome that should contain it");
            Assert.LessOrEqual(inner.yMax, outer.yMax + tolerance, $"{what} runs past the chrome's bottom edge - it is being clipped out of the overlay");
        }

        // A PointerDown/PointerUp pair does not work here: Clickable only fires on the up event if
        // the panel's picking still reports the element as the one under the pointer, and forcing
        // target on a synthetic event skips the picking that state depends on. NavigationSubmitEvent
        // is the real alternative rather than a workaround - Button's constructor registers
        // OnNavigationSubmit, which calls clickable.SimulateSingleClick directly. Confirmed by
        // decompiling UnityEngine.UIElementsModule.dll for 6000.3.11f1, not assumed from docs.
        private static void Click(Button button)
        {
            Assert.IsNotNull(button, "guard: cannot click a button the query did not find");

            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }
    }
}
