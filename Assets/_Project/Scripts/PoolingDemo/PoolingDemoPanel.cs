using System;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace Company.ChestGame.Pooling.Demo
{
    // A self-contained demonstration of the pooling assembly, deliberately unconnected to the chest
    // game: it is dropped into a scene as a prefab, races whatever prefab it is given, and nothing
    // in the game holds a reference to it. See docs/design-decisions.md for why they are split.
    //
    // The chrome is authored - PoolingDemo.uxml for the tree, PoolingDemo.uss for the styling - and
    // this class only binds to it: query by name, set text, add and remove a class. The lanes stay
    // uGUI and stay built at runtime, because they hold real pooled Components and a VisualElement
    // can host neither a Component nor a GameObject.
    public sealed class PoolingDemoPanel : MonoBehaviour
    {
        private static readonly int[] BoardSizes = { 8, 100, 500, 2000 };

        // The same number every lane races under - see PoolRace<T> for why that equality is the
        // whole point.
        private const double FillBudgetMilliseconds = 2d;

        // The largest selectable board size. Every lane's pool is bounded to this once, so switching
        // board size between races never has to rebuild a pool, only trim it.
        private const int MaxBoardSize = 2000;

        private const string SelectedClass = "is-selected";

        [Header("Authored chrome")]
        [SerializeField] private UIDocument _document;

        [Header("Lanes (uGUI - they hold real pooled Components)")]
        [SerializeField] private Canvas _lanesCanvas;
        [SerializeField] private RectTransform _lanesRoot;

        // In PoolRaceLaneFactory.AllStrategies order: slot i is where strategy i builds its holder
        // and its fill parent.
        [SerializeField] private RectTransform[] _laneSlots;

        // What the race spawns. Typed as RectTransform rather than a concrete component so any UI
        // prefab can be dropped in without this class knowing what it is.
        [Header("What to race")]
        [SerializeField] private RectTransform _itemPrefab;

        private IPoolRaceController _race;
        private PoolStrategy[] _laneOrder;

        private VisualElement _chrome;
        private VisualElement _lanesSlot;
        private Button _toggleButton;
        private bool _expanded;

        private int _boardSizeIndex = 1;
        private FillMode _fillMode = FillMode.Cold;
        private bool _solo;
        private PoolStrategy _soloStrategy = PoolStrategy.ActivationPool;

        private Button[] _boardSizeButtons;
        private Button[] _strategyButtons;
        private Button _fillModeButton;
        private Button _modeButton;
        private Label[] _metricsLabels;
        private Label[] _headlineLabels;
        private Label _readoutLabel;
        private Label _peakFrameLabel;

        private float _peakFrameSeconds;

        // Start, not Awake: UIDocument creates its rootVisualElement in OnEnable, and Awake runs
        // before any OnEnable on the same object, so binding earlier reads a null tree.
        private void Start()
        {
            _laneOrder = PoolRaceLaneFactory.AllStrategies;

            if (_document == null) throw PoolRaceException.NoDocument();
            if (_itemPrefab == null) throw PoolRaceException.NoItemPrefab();
            if (_laneSlots == null || _laneSlots.Length != _laneOrder.Length)
            {
                throw PoolRaceException.LaneSlotCountMismatch(_laneOrder.Length, _laneSlots?.Length ?? 0);
            }

            BindChrome();
            BuildRace();

            // Collapsed before the first frame anyone could see, through the same path a tap takes
            // so there is one definition of what collapsed means.
            _expanded = true;
            ToggleExpanded();
        }

        // Both of these die with the prefab in practice, so this is teardown discipline rather than
        // a live leak.
        private void OnDestroy()
        {
            if (_lanesSlot != null) _lanesSlot.UnregisterCallback<GeometryChangedEvent>(OnLanesSlotGeometryChanged);
            if (_race != null) _race.OnRaceCompleted -= OnRaceCompleted;

            _race?.Dispose();
        }

        private void OnLanesSlotGeometryChanged(GeometryChangedEvent _) => PlaceLanes();

        private void Update()
        {
            // Time.unscaledDeltaTime rather than the clock the race runs on: this measures the real
            // frame this MonoBehaviour is living in, and the orchestration has to stay testable
            // against a fake clock.
            if (_race == null || !_race.IsRunning) return;

            _peakFrameSeconds = Mathf.Max(_peakFrameSeconds, Time.unscaledDeltaTime);
            _peakFrameLabel.text = $"Peak frame time (real, this device): {_peakFrameSeconds * 1000f:F1} ms";
        }

        // --- Binding to the authored tree --------------------------------------------------------

        private void BindChrome()
        {
            VisualElement root = _document.rootVisualElement;

            // A full-screen root with the default picking mode would swallow every tap meant for the
            // game underneath, even collapsed with nothing drawn. pickingMode is per-element, not
            // inherited, so this opts out the root while the chrome and toggle keep theirs.
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
            _fillModeButton.clicked += CycleFillMode;

            _modeButton = Required<Button>(root, "mode-button");
            _modeButton.clicked += ToggleSolo;

            _boardSizeButtons = new Button[BoardSizes.Length];
            for (int i = 0; i < BoardSizes.Length; i++)
            {
                int index = i;
                _boardSizeButtons[i] = Required<Button>(root, $"size-{i}");
                _boardSizeButtons[i].text = BoardSizes[i].ToString();
                _boardSizeButtons[i].clicked += () => SetBoardSize(index);
            }

            _strategyButtons = new Button[_laneOrder.Length];
            _metricsLabels = new Label[_laneOrder.Length];
            _headlineLabels = new Label[_laneOrder.Length];
            for (int i = 0; i < _laneOrder.Length; i++)
            {
                int index = i;
                _strategyButtons[i] = Required<Button>(root, $"strategy-{i}");
                _strategyButtons[i].text = ShortNameOf(_laneOrder[i]);
                _strategyButtons[i].clicked += () => SetSoloStrategy(index);

                // Written from the enum rather than trusted from the UXML, so a reordering of
                // AllStrategies cannot leave a card labelled as the wrong strategy.
                Required<Label>(root, $"lane-name-{i}").text = _laneOrder[i].ToString();
                _headlineLabels[i] = Required<Label>(root, $"lane-headline-{i}");
                _metricsLabels[i] = Required<Label>(root, $"lane-metrics-{i}");
            }

            // The uGUI lanes follow whatever the stylesheet leaves over, rather than a hard-coded
            // height kept in step with it by hand.
            _lanesSlot.RegisterCallback<GeometryChangedEvent>(OnLanesSlotGeometryChanged);

            RefreshControlLabels();
            ShowIdleReadout();
        }

        // A missing name is a broken .uxml, not a state to limp along in: the alternative is a
        // NullReferenceException further away with nothing naming the element that went missing.
        private static T Required<T>(VisualElement root, string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null) throw PoolRaceException.MissingElement(typeof(T).Name, name);

            return element;
        }

        private void BuildRace()
        {
            Transform[] laneRoots = new Transform[_laneSlots.Length];
            for (int i = 0; i < _laneSlots.Length; i++) laneRoots[i] = _laneSlots[i];

            PoolRaceLane<RectTransform>[] lanes = PoolRaceLaneFactory.BuildAll(_itemPrefab, laneRoots, MaxBoardSize);

            // Its own clock, not an injected one: this panel is dropped into a scene and is not
            // part of anything's object graph. Linked to its own destroy token, so a race in flight
            // when this is torn down unwinds instead of filling into lanes that are going away.
            PoolRace<RectTransform> race = new(lanes, new UnityGameClock(), FillBudgetMilliseconds, this.GetCancellationTokenOnDestroy());
            race.OnRaceCompleted += OnRaceCompleted;
            _race = race;
        }

        // The stylesheet owns the layout; this only copies the answer onto the uGUI side. Both are
        // laid out against the same reference resolution - the prefab's CanvasScaler and its
        // PanelSettings are authored to match - so a logical pixel here is a canvas pixel there.
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

        // --- Collapsing and expanding ------------------------------------------------------------

    // display:None takes the chrome out of both layout and picking, and disabling the lanes' own
        // Canvas hides the uGUI side without deactivating a single GameObject - which matters, because
        // ParkedPool refuses an inactive holder.
        //
        // The floating toggle and the Close button are the same control in two states, and exactly one
        // is ever on screen: the toggle's band runs through a control row, so showing both would have
        // it sitting on that row's last button.
        private void ToggleExpanded()
        {
            _expanded = !_expanded;

            _chrome.style.display = _expanded ? DisplayStyle.Flex : DisplayStyle.None;
            _toggleButton.style.display = _expanded ? DisplayStyle.None : DisplayStyle.Flex;
            if (_lanesCanvas != null) _lanesCanvas.enabled = _expanded;

            if (_expanded) PlaceLanes();
        }

        // --- Control callbacks -------------------------------------------------------------------

        private void SetBoardSize(int index)
        {
            _boardSizeIndex = index;
            RefreshControlLabels();
        }

        // Cycles Cold -> Prewarmed -> Reuse -> Cold.
        private void CycleFillMode()
        {
            _fillMode = _fillMode switch
            {
                FillMode.Cold => FillMode.Prewarmed,
                FillMode.Prewarmed => FillMode.Reuse,
                _ => FillMode.Cold
            };
            RefreshControlLabels();
        }

        private void ToggleSolo()
        {
            _solo = !_solo;
            RefreshControlLabels();
        }

        private void SetSoloStrategy(int index)
        {
            _soloStrategy = _laneOrder[index];
            RefreshControlLabels();
        }

        private void OnRunClicked()
        {
            _peakFrameSeconds = 0f;
            _peakFrameLabel.text = "Peak frame time: -";
            _readoutLabel.text = _solo
                ? $"Running solo: {_soloStrategy} ({_fillMode})..."
                : $"Running all four ({_fillMode})...";

            _race.StartRace(BoardSizes[_boardSizeIndex], _fillMode, _solo, _soloStrategy);
        }

        // Selection is a class the stylesheet reacts to, not a colour set from here: this file names
        // a state, PoolingDemo.uss decides what it looks like.
        private void RefreshControlLabels()
        {
            for (int i = 0; i < _boardSizeButtons.Length; i++)
            {
                _boardSizeButtons[i].EnableInClassList(SelectedClass, i == _boardSizeIndex);
            }

            for (int i = 0; i < _strategyButtons.Length; i++)
            {
                _strategyButtons[i].EnableInClassList(SelectedClass, _solo && _laneOrder[i] == _soloStrategy);
            }

            // Captioned rather than bare, because a button reading only "Cold" says nothing about
            // what it is the cold setting of.
            _fillModeButton.text = $"Fill: {_fillMode}";
            _modeButton.text = _solo ? "Mode: Solo" : "Mode: All Four";
        }

        // The segmented control's labels. Written out, the four enum names run to 46 characters and
        // no phone-width row holds them; the full name still leads each lane card.
        private static string ShortNameOf(PoolStrategy strategy) => strategy switch
        {
            PoolStrategy.ActivationPool => "Activation",
            PoolStrategy.ParkedPool => "Parked",
            PoolStrategy.UnityPool => "Unity",
            PoolStrategy.DirectSpawner => "Direct",
            _ => strategy.ToString()
        };

        // --- Reading the result back -------------------------------------------------------------

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

                // Elapsed is promoted into the card's one large figure: it is the number a race is
                // watched for.
                _headlineLabels[index].text = $"{lane.ElapsedMilliseconds:F1} ms";
                _metricsLabels[index].text =
                    $"Placed {lane.PlacedCount}/{lane.RequestedCount}\n" +
                    $"Created {lane.Instantiated}\n" +
                    $"Destroyed {lane.Destroyed}\n" +
                    $"Frames {lane.FramesUsed}";
            }

            // The caveat, stated where it is read: four lanes sharing one clock means no lane's
            // elapsed time here is what it would cost running alone.
            string modeCaveat = result.Solo
                ? $"Solo mode - {result.Lanes[0].Strategy} ran alone. These figures are its own, uncontended."
                : "All four, simultaneous - four lanes share these frames, so no lane's elapsed time here " +
                  "is what it would cost running alone. This proves the ordering between strategies, not " +
                  "any one standalone number; switch to solo for that.";

            // result.FillMode, not the field: tapping Fill while a race is in flight would otherwise
            // label the figures that land with a mode that did not produce them.
            _readoutLabel.text = $"[{result.FillMode}] {modeCaveat}";
        }
    }
}
