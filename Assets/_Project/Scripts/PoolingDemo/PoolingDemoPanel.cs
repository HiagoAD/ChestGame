using System;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace Company.ChestGame.Pooling.Demo
{
    // A self-contained demonstration of what the pooling assembly does, and deliberately nothing to
    // do with the chest game: it is dropped into a scene as a prefab, races whatever prefab it is
    // given, and nothing in the game holds a reference to it. The minigame uses IPrefabPool the same
    // way anything else would; this shows the four strategies against each other, and the two are
    // not wired together in either direction.
    //
    // The chrome is authored - PoolingDemo.uxml for the tree, PoolingDemo.uss for the styling - and
    // this class only binds to it: query by name, set text, add and remove a class. Nothing here
    // decides what anything looks like. The lanes stay uGUI and stay built at runtime, because they
    // hold real pooled Components and a VisualElement can host neither a Component nor a GameObject.
    public sealed class PoolingDemoPanel : MonoBehaviour
    {
        private static readonly int[] BoardSizes = { 8, 100, 500, 2000 };

        // Small enough that a fill never reads as a hitch on its own, and the same number every lane
        // races under - see PoolRace<T> for why that equality is the whole point.
        private const double FillBudgetMilliseconds = 2d;

        // The largest selectable board size. Every lane's pool is bounded to this once, at build
        // time, so switching board size between races never has to rebuild a pool - only trim it.
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

        // What the race spawns. Typed as RectTransform rather than as a concrete component so any UI
        // prefab can be dropped in - the chest element included - without this class knowing what it
        // is. The tile it ships with is a plain square, which reads better at two thousand items than
        // any detailed sprite does.
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

        // Start, not Awake: UIDocument creates its rootVisualElement in OnEnable, and component
        // Awake runs before any OnEnable on the same object - so binding any earlier than this reads
        // a null tree. Nothing here needs to happen before the first frame; the panel is collapsed
        // at the end of it either way.
        private void Start()
        {
            _laneOrder = PoolRaceLaneFactory.AllStrategies;

            if (_document == null) throw new PoolRaceException("PoolingDemoPanel has no UIDocument assigned.");
            if (_itemPrefab == null) throw new PoolRaceException("PoolingDemoPanel has no item prefab assigned - there is nothing to race.");
            if (_laneSlots == null || _laneSlots.Length != _laneOrder.Length)
            {
                throw new PoolRaceException(
                    $"PoolingDemoPanel needs one lane slot per strategy ({_laneOrder.Length}); it has {(_laneSlots == null ? 0 : _laneSlots.Length)}.");
            }

            BindChrome();
            BuildRace();

            // Collapsed before the first frame anyone could see. Set through the same path a tap
            // takes, so there is one definition of what collapsed means.
            _expanded = true;
            ToggleExpanded();
        }

        private void OnDestroy() => _race?.Dispose();

        private void Update()
        {
            // Time.unscaledDeltaTime rather than the clock the race runs on, on purpose: this is a
            // real-engine measurement of the frame this MonoBehaviour is actually living in, and the
            // orchestration must not depend on it to stay testable against a fake clock.
            if (_race == null || !_race.IsRunning) return;

            _peakFrameSeconds = Mathf.Max(_peakFrameSeconds, Time.unscaledDeltaTime);
            _peakFrameLabel.text = $"Peak frame time (real, this device): {_peakFrameSeconds * 1000f:F1} ms";
        }

        // --- Binding to the authored tree --------------------------------------------------------

        private void BindChrome()
        {
            VisualElement root = _document.rootVisualElement;

            // A full-screen root with the default picking mode would swallow every tap meant for the
            // game underneath even while collapsed and nothing is drawn. pickingMode is per-element,
            // not inherited, so this opts the root itself out while the chrome and toggle keep theirs.
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
                // AllStrategies can never leave a card labelled as the wrong strategy.
                Required<Label>(root, $"lane-name-{i}").text = _laneOrder[i].ToString();
                _headlineLabels[i] = Required<Label>(root, $"lane-headline-{i}");
                _metricsLabels[i] = Required<Label>(root, $"lane-metrics-{i}");
            }

            // The uGUI lanes follow whatever the stylesheet decides is left over, rather than being
            // offset by a hard-coded height that has to be kept in step with it by hand.
            _lanesSlot.RegisterCallback<GeometryChangedEvent>(_ => PlaceLanes());

            RefreshControlLabels();
            ShowIdleReadout();
        }

        // A missing name is a broken .uxml, not a state to limp along in: every one of these is
        // wired on the next line, so the alternative is a NullReferenceException from somewhere
        // further away with nothing in it naming the element that went missing.
        private static T Required<T>(VisualElement root, string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null) throw new PoolRaceException($"PoolingDemo.uxml has no {typeof(T).Name} named '{name}'.");

            return element;
        }

        private void BuildRace()
        {
            Transform[] laneRoots = new Transform[_laneSlots.Length];
            for (int i = 0; i < _laneSlots.Length; i++) laneRoots[i] = _laneSlots[i];

            PoolRaceLane<RectTransform>[] lanes = PoolRaceLaneFactory.BuildAll(_itemPrefab, laneRoots, MaxBoardSize);

            // Its own clock, not an injected one: this panel is dropped into a scene and is not part
            // of anything's object graph. Linked to its own destroy token the way the minigame's
            // board fill is, so a race still in flight when this is torn down unwinds instead of
            // filling into lanes that are going away.
            PoolRace<RectTransform> race = new(lanes, new UnityGameClock(), FillBudgetMilliseconds, this.GetCancellationTokenOnDestroy());
            race.OnRaceCompleted += OnRaceCompleted;
            _race = race;
        }

        // The stylesheet owns the layout, including how much room is left under the cards; this only
        // copies the answer onto the uGUI side. Both are laid out against the same reference
        // resolution - the prefab's CanvasScaler and its PanelSettings are authored to match - so a
        // logical pixel here is a canvas pixel there, and the panel's top-left origin is the
        // canvas's too.
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
        // Canvas hides the uGUI side without deactivating a single GameObject - which matters,
        // because ParkedPool refuses an inactive holder and everything parked under one would fire
        // exactly the OnDisable that pool exists to avoid.
        //
        // The floating toggle and the Close button in the title bar are the same control in two
        // states, and exactly one is ever on screen: the toggle's band runs through a control row,
        // so a demo showing both would have it sitting on that row's last button.
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

        // Cycles Cold -> Prewarmed -> Reuse -> Cold. Three states on one button rather than a fourth
        // control, since only one is ever active at a time.
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

        // Selection is a class the stylesheet reacts to, not a colour set from here - which is the
        // whole point of the chrome being authored: this file names a state, PoolingDemo.uss decides
        // what that state looks like.
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
        // no phone-width row holds them; the full name still leads each lane card, so nothing is only
        // ever shown abbreviated.
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

                // Elapsed is promoted out of the list and into the card's one large figure: it is the
                // number a race is watched for, and reading it off the third line of four made every
                // lane look the same until it was actually read.
                _headlineLabels[index].text = $"{lane.ElapsedMilliseconds:F1} ms";
                _metricsLabels[index].text =
                    $"Placed {lane.PlacedCount}/{lane.RequestedCount}\n" +
                    $"Created {lane.Instantiated}\n" +
                    $"Destroyed {lane.Destroyed}\n" +
                    $"Frames {lane.FramesUsed}";
            }

            // The honest caveat, stated where it is read: four lanes sharing one clock means no
            // lane's elapsed time here is what it would cost running alone.
            string modeCaveat = result.Solo
                ? $"Solo mode - {result.Lanes[0].Strategy} ran alone. These figures are its own, uncontended."
                : "All four, simultaneous - four lanes share these frames, so no lane's elapsed time here " +
                  "is what it would cost running alone. This proves the ordering between strategies, not " +
                  "any one standalone number; switch to solo for that.";

            // Named here too, and not only on the Run button: Reuse is the one mode that shows what
            // pooling is actually for, and it only means anything read against what a second press
            // just did.
            _readoutLabel.text = $"[{_fillMode}] {modeCaveat}";
        }
    }
}
