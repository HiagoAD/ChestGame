using System;
using System.Collections.Generic;
using Company.ChestGame.Common;
using Company.ChestGame.Mvc;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace Company.ChestGame.Pooling.Demo
{
    /// <summary>
    /// A self-contained demonstration of the pooling assembly. It is dropped into a scene as a
    /// prefab, races whatever prefab it is given, and nothing in the game holds a reference to it.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Assembly layout". See docs/mvc.md.
    /// </remarks>
    public sealed class PoolingDemoPanel : ViewBase<IPoolRaceController>
    {
        /// <summary>
        /// The fill budget every lane races under.
        /// </summary>
        /// <remarks>
        /// See docs/pooling.md, "PoolRace, and why simultaneous lanes are not solo timings".
        /// </remarks>
        private const double FillBudgetMilliseconds = 2d;

        private const string SelectedClass = "is-selected";

        [Header("Authored chrome")]
        [SerializeField] private UIDocument _document;

        [Header("Lanes (uGUI - they hold real pooled Components)")]
        [SerializeField] private Canvas _lanesCanvas;
        [SerializeField] private RectTransform _lanesRoot;

        /// <summary>
        /// In <see cref="PoolRaceLaneFactory.AllStrategies"/> order: slot i is where strategy i
        /// builds its holder and its fill parent.
        /// </summary>
        [SerializeField] private RectTransform[] _laneSlots;

        /// <summary>
        /// What the race spawns. Accepts any RectTransform-rooted UI prefab; this class does not
        /// need to know its concrete type.
        /// </summary>
        [Header("What to race")]
        [SerializeField] private RectTransform _itemPrefab;

        private PoolStrategy[] _laneOrder;

        private VisualElement _chrome;
        private VisualElement _lanesSlot;
        private Button _toggleButton;
        private bool _expanded;

        private Button[] _boardSizeButtons;
        private Button[] _strategyButtons;
        private Button _fillModeButton;
        private Button _modeButton;
        private Label[] _metricsLabels;
        private Label[] _headlineLabels;
        private Label _readoutLabel;
        private Label _peakFrameLabel;

        /// <summary>
        /// Validates the authored references, builds and binds the controller, binds the chrome,
        /// and starts collapsed.
        /// </summary>
        /// <exception cref="PoolRaceException">
        /// When <c>_document</c> or <c>_itemPrefab</c> is unassigned, <c>_laneSlots</c>' length does
        /// not match <see cref="PoolRaceLaneFactory.AllStrategies"/>, or the authored UI tree is
        /// missing an element this panel binds to.
        /// </exception>
        /// <remarks>
        /// See docs/pooling.md, "PoolingDemoPanel's startup order".
        /// </remarks>
        private void Start()
        {
            _laneOrder = PoolRaceLaneFactory.AllStrategies;

            if (_document == null) throw PoolRaceException.NoDocument();
            if (_itemPrefab == null) throw PoolRaceException.NoItemPrefab();
            if (_laneSlots == null || _laneSlots.Length != _laneOrder.Length)
            {
                throw PoolRaceException.LaneSlotCountMismatch(_laneOrder.Length, _laneSlots?.Length ?? 0);
            }

            Bind(BuildController());
            BindChrome();

            _expanded = true;
            ToggleExpanded();
        }

        /// <summary>
        /// Builds the lanes from the authored item prefab and lane slots, bounding every lane's
        /// pool to <see cref="PoolRace{T}.MaxBoardSize"/>, and races them on their own
        /// <see cref="UnityGameClock"/> linked to this panel's destruction.
        /// </summary>
        /// <remarks>
        /// See docs/pooling.md, "PoolingDemoPanel's startup order".
        /// </remarks>
        private IPoolRaceController BuildController()
        {
            Transform[] laneRoots = new Transform[_laneSlots.Length];
            for (int i = 0; i < _laneSlots.Length; i++) laneRoots[i] = _laneSlots[i];

            PoolRaceLane<RectTransform>[] lanes =
                PoolRaceLaneFactory.BuildAll(_itemPrefab, laneRoots, PoolRace<RectTransform>.MaxBoardSize);

            return new PoolRace<RectTransform>(lanes, new UnityGameClock(), FillBudgetMilliseconds, this.GetCancellationTokenOnDestroy());
        }

        protected override void OnBind()
        {
            Controller.OnRaceCompleted += OnRaceCompleted;
            Controller.OnSelectionChanged += RefreshControlLabels;
        }

        /// <summary>Unsubscribes from the controller and disposes it, since this view owns it.</summary>
        protected override void OnUnbind()
        {
            Controller.OnRaceCompleted -= OnRaceCompleted;
            Controller.OnSelectionChanged -= RefreshControlLabels;
            Controller.Dispose();
        }

        /// <remarks>
        /// See docs/pooling.md, "PoolingDemoPanel's startup order".
        /// </remarks>
        protected override void OnDestroy()
        {
            if (_lanesSlot != null) _lanesSlot.UnregisterCallback<GeometryChangedEvent>(OnLanesSlotGeometryChanged);

            base.OnDestroy();
        }

        private void OnLanesSlotGeometryChanged(GeometryChangedEvent _) => PlaceLanes();

        /// <remarks>
        /// See docs/pooling.md, "PoolingDemoPanel's startup order".
        /// </remarks>
        private void Update()
        {
            if (!IsBound || !Controller.IsRunning) return;

            Controller.Tick(Time.unscaledDeltaTime);
            _peakFrameLabel.text = $"Peak frame time (real, this device): {Controller.PeakFrameSeconds * 1000f:F1} ms";
        }

        /// <remarks>
        /// See docs/pooling.md, "PoolingDemoPanel's UI binding traps".
        /// </remarks>
        private void BindChrome()
        {
            VisualElement root = _document.rootVisualElement;

            root.pickingMode = PickingMode.Ignore;

            _chrome = Required<VisualElement>(root, "chrome");
            _lanesSlot = Required<VisualElement>(root, "lanes-slot");
            _toggleButton = Required<Button>(root, "toggle-button");
            _readoutLabel = Required<Label>(root, "readout-label");
            _peakFrameLabel = Required<Label>(root, "peak-frame-label");

            _toggleButton.clicked += ToggleExpanded;
            Required<Button>(root, "close-button").clicked += ToggleExpanded;
            Required<Button>(root, "run-button").clicked += OnRunClicked;

            _fillModeButton = Required<Button>(root, "fill-mode-button");
            _fillModeButton.clicked += Controller.CycleFillMode;

            _modeButton = Required<Button>(root, "mode-button");
            _modeButton.clicked += Controller.ToggleSolo;

            IReadOnlyList<int> boardSizes = Controller.BoardSizes;
            _boardSizeButtons = new Button[boardSizes.Count];
            for (int i = 0; i < boardSizes.Count; i++)
            {
                int index = i;
                _boardSizeButtons[i] = Required<Button>(root, $"size-{i}");
                _boardSizeButtons[i].text = boardSizes[i].ToString();
                _boardSizeButtons[i].clicked += () => Controller.SetBoardSize(index);
            }

            _strategyButtons = new Button[_laneOrder.Length];
            _metricsLabels = new Label[_laneOrder.Length];
            _headlineLabels = new Label[_laneOrder.Length];
            for (int i = 0; i < _laneOrder.Length; i++)
            {
                int index = i;
                _strategyButtons[i] = Required<Button>(root, $"strategy-{i}");
                _strategyButtons[i].text = ShortNameOf(_laneOrder[i]);
                _strategyButtons[i].clicked += () => Controller.SetSoloStrategy(_laneOrder[index]);

                Required<Label>(root, $"lane-name-{i}").text = _laneOrder[i].ToString();
                _headlineLabels[i] = Required<Label>(root, $"lane-headline-{i}");
                _metricsLabels[i] = Required<Label>(root, $"lane-metrics-{i}");
            }

            _lanesSlot.RegisterCallback<GeometryChangedEvent>(OnLanesSlotGeometryChanged);

            RefreshControlLabels();
            ShowIdleReadout();
        }

        /// <exception cref="PoolRaceException">
        /// When no element named <paramref name="name"/> of type <typeparamref name="T"/> exists
        /// under <paramref name="root"/>.
        /// </exception>
        /// <remarks>
        /// See docs/pooling.md, "PoolingDemoPanel's UI binding traps".
        /// </remarks>
        private static T Required<T>(VisualElement root, string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null) throw PoolRaceException.MissingElement(typeof(T).Name, name);

            return element;
        }

        /// <remarks>
        /// See docs/pooling.md, "PoolingDemoPanel's UI binding traps".
        /// </remarks>
        private void PlaceLanes()
        {
            if (_lanesRoot == null) return;

            Rect slot = _lanesSlot.worldBound;
            if (slot.width <= 0f || slot.height <= 0f) return;

            _lanesRoot.anchorMin = new Vector2(0f, 1f);
            _lanesRoot.anchorMax = new Vector2(0f, 1f);
            _lanesRoot.pivot = new Vector2(0f, 1f);
            _lanesRoot.anchoredPosition = new Vector2(slot.xMin, -slot.yMin);
            _lanesRoot.sizeDelta = new Vector2(slot.width, slot.height);
        }

        /// <remarks>
        /// See docs/pooling.md, "PoolingDemoPanel's collapse mechanics".
        /// </remarks>
        private void ToggleExpanded()
        {
            _expanded = !_expanded;

            _chrome.style.display = _expanded ? DisplayStyle.Flex : DisplayStyle.None;
            _toggleButton.style.display = _expanded ? DisplayStyle.None : DisplayStyle.Flex;
            if (_lanesCanvas != null) _lanesCanvas.enabled = _expanded;

            if (_expanded) PlaceLanes();
        }

        private void OnRunClicked()
        {
            _peakFrameLabel.text = "Peak frame time: -";
            _readoutLabel.text = Controller.Solo
                ? $"Running solo: {Controller.SoloStrategy} ({Controller.FillMode})..."
                : $"Running all four ({Controller.FillMode})...";

            Controller.StartRace();
        }

        /// <remarks>
        /// See docs/pooling.md, "PoolingDemoPanel's control and readout wiring".
        /// </remarks>
        private void RefreshControlLabels()
        {
            for (int i = 0; i < _boardSizeButtons.Length; i++)
            {
                _boardSizeButtons[i].EnableInClassList(SelectedClass, i == Controller.BoardSizeIndex);
            }

            for (int i = 0; i < _strategyButtons.Length; i++)
            {
                _strategyButtons[i].EnableInClassList(SelectedClass, Controller.Solo && _laneOrder[i] == Controller.SoloStrategy);
            }

            _fillModeButton.text = $"Fill: {Controller.FillMode}";
            _modeButton.text = Controller.Solo ? "Mode: Solo" : "Mode: All Four";
        }

        /// <summary>
        /// The segmented control's label for <paramref name="strategy"/>.
        /// </summary>
        /// <remarks>
        /// See docs/pooling.md, "PoolingDemoPanel's control and readout wiring".
        /// </remarks>
        private static string ShortNameOf(PoolStrategy strategy) => strategy switch
        {
            PoolStrategy.ActivationPool => "Activation",
            PoolStrategy.ParkedPool => "Parked",
            PoolStrategy.UnityPool => "Unity",
            PoolStrategy.DirectSpawner => "Direct",
            _ => strategy.ToString()
        };

        private void ShowIdleReadout()
        {
            _readoutLabel.text =
                "Press Run to start a race. At 8 items this measures nothing - pick 500 or 2000 to see a real difference.";
            for (int i = 0; i < _metricsLabels.Length; i++)
            {
                _metricsLabels[i].text = "not run yet";
                _headlineLabels[i].text = "-";
            }
        }

        /// <remarks>
        /// See docs/pooling.md, "PoolingDemoPanel's control and readout wiring".
        /// See docs/pooling.md, "RaceResult, and why Solo and FillMode travel with it".
        /// </remarks>
        private void OnRaceCompleted(RaceResult result)
        {
            for (int i = 0; i < _laneOrder.Length; i++)
            {
                _metricsLabels[i].text = "not run this race";
                _headlineLabels[i].text = "-";
            }

            foreach (LaneMetrics lane in result.Lanes)
            {
                int index = Array.IndexOf(_laneOrder, lane.Strategy);

                _headlineLabels[index].text = $"{lane.ElapsedMilliseconds:F1} ms";
                _metricsLabels[index].text =
                    $"Placed {lane.PlacedCount}/{lane.RequestedCount}\n" +
                    $"Created {lane.Instantiated}\n" +
                    $"Destroyed {lane.Destroyed}\n" +
                    $"Frames {lane.FramesUsed}";
            }

            string modeCaveat = result.Solo
                ? $"Solo mode - {result.Lanes[0].Strategy} ran alone. These figures are its own, uncontended."
                : "All four, simultaneous - four lanes share these frames, so no lane's elapsed time here " +
                  "is what it would cost running alone. This proves the ordering between strategies, not " +
                  "any one standalone number; switch to solo for that.";

            _readoutLabel.text = $"[{result.FillMode}] {modeCaveat}";
        }
    }
}
