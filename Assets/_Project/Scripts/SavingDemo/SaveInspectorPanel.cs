using System;
using Company.ChestGame.Mvc;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Company.ChestGame.Saving.Demo
{
    /// <summary>
    /// The save inspector's view: binds the tree authored in SaveInspector.uxml by name, forwards
    /// button presses to an <see cref="ISaveInspectorController"/> it builds itself, and renders that
    /// controller's state. Decides nothing about the save or tamper flows themselves.
    /// </summary>
    /// <remarks>
    /// This view builds its own controller and so, unlike a view handed an already-built controller,
    /// owns disposing it: see <see cref="OnUnbind"/>. See docs/mvc.md. See docs/saving.md, "The save
    /// inspector".
    /// </remarks>
    public sealed class SaveInspectorPanel : ViewBase<ISaveInspectorController>
    {
        private const string SelectedClass = "is-selected";
        private const string TamperAcceptedClass = "tamper-readout--accepted";
        private const string TamperRejectedClass = "tamper-readout--rejected";

        /// <summary>
        /// Upper bound, in characters, on how much of a rendered save this panel displays before
        /// truncating.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "SaveInspectorPanel, binding, keys and truncation".
        /// </remarks>
        private const int MaxRenderedCharacters = 4000;

        /// <summary>
        /// The save inspector's full chrome panel. Must be assigned in the inspector: <see cref="Start"/>
        /// throws if this or <see cref="_toggleDocument"/> is null.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "Two overlays in one scene, and why the save inspector uses two documents".
        /// </remarks>
        [Header("Authored chrome")]
        [SerializeField] private UIDocument _document;

        /// <summary>
        /// The collapsed toggle button's own document. Must be assigned in the inspector:
        /// <see cref="Start"/> throws if this or <see cref="_document"/> is null.
        /// </summary>
        [SerializeField] private UIDocument _toggleDocument;

        private VisualElement _chrome;
        private Button _toggleButton;
        private bool _expanded;

        private Button[] _storageButtons;
        private Button[] _codecButtons;
        private Button[] _protectionButtons;

        private Button _saveButton;
        private Button _tamperButton;
        private Label _readoutLabel;
        private Label _timingsLabel;
        private Label _bytesLabel;
        private VisualElement _tamperReadout;
        private Label _tamperLabel;

        /// <summary>
        /// Builds the controller, binds the authored UI tree, and opens the panel collapsed.
        /// </summary>
        /// <exception cref="SaveInspectorException">
        /// When <see cref="_document"/> or <see cref="_toggleDocument"/> is not assigned.
        /// </exception>
        /// <remarks>
        /// See docs/saving.md, "SaveInspectorPanel, binding, keys and truncation".
        /// </remarks>
        private void Start()
        {
            if (_document == null) throw SaveInspectorException.NoDocument();
            if (_toggleDocument == null) throw SaveInspectorException.NoToggleDocument();

            Bind(BuildController());
            BindChrome();

            _expanded = true;
            ToggleExpanded();
        }

        private static ISaveInspectorController BuildController() => new SaveInspectorController();

        protected override void OnBind()
        {
            Controller.OnSelectionChanged += RefreshControlLabels;
            Controller.OnBusyChanged += RenderBusy;
            Controller.OnSaveCompleted += ShowSaveResult;
            Controller.OnSaveFailed += ShowSaveFailure;
            Controller.OnTamperCompleted += ShowTamperResult;
            Controller.OnTamperFailed += ShowTamperFailure;
        }

        /// <summary>Unsubscribes from the controller and disposes it, since this view owns it.</summary>
        protected override void OnUnbind()
        {
            Controller.OnSelectionChanged -= RefreshControlLabels;
            Controller.OnBusyChanged -= RenderBusy;
            Controller.OnSaveCompleted -= ShowSaveResult;
            Controller.OnSaveFailed -= ShowSaveFailure;
            Controller.OnTamperCompleted -= ShowTamperResult;
            Controller.OnTamperFailed -= ShowTamperFailure;
            Controller.Dispose();
        }

        /// <summary>
        /// Wires every control in the authored tree to its handler and puts the panel in its idle
        /// state.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "SaveInspectorPanel, binding, keys and truncation".
        /// </remarks>
        private void BindChrome()
        {
            VisualElement root = _document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;

            VisualElement toggleRoot = _toggleDocument.rootVisualElement;
            toggleRoot.pickingMode = PickingMode.Ignore;

            _chrome = Required<VisualElement>(root, "chrome");
            _toggleButton = Required<Button>(toggleRoot, "toggle-button");
            _readoutLabel = Required<Label>(root, "readout-label");
            _timingsLabel = Required<Label>(root, "timings-label");
            _bytesLabel = Required<Label>(root, "bytes-label");
            _tamperReadout = Required<VisualElement>(root, "tamper-readout");
            _tamperLabel = Required<Label>(root, "tamper-label");

            _toggleButton.clicked += ToggleExpanded;
            Required<Button>(root, "close-button").clicked += ToggleExpanded;

            _saveButton = Required<Button>(root, "save-button");
            _saveButton.clicked += OnSaveClicked;

            _tamperButton = Required<Button>(root, "tamper-button");
            _tamperButton.clicked += OnTamperClicked;

            _storageButtons = BindSegment(root, "storage", Controller.Storages.Count,
                i => ShortNameOf(Controller.Storages[i]), Controller.SetStorage);
            _codecButtons = BindSegment(root, "codec", Controller.Codecs.Count,
                i => ShortNameOf(Controller.Codecs[i]), Controller.SetCodec);
            _protectionButtons = BindSegment(root, "protection", Controller.Protections.Count,
                i => Controller.Protections[i].ToString(), Controller.SetProtection);

            RenderBusy(Controller.IsBusy);
            RefreshControlLabels();
            ShowIdleReadout();
            ShowIdleTamperReadout();
        }

        /// <summary>
        /// Builds one segmented control's buttons, labeling and wiring each from the enum rather
        /// than whatever SaveInspector.uxml happens to have authored at that index.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "The three selection enums are append-only".
        /// </remarks>
        private static Button[] BindSegment(VisualElement root, string prefix, int count, Func<int, string> labelFor, Action<int> onSelect)
        {
            Button[] buttons = new Button[count];
            for (int i = 0; i < count; i++)
            {
                int index = i;
                buttons[i] = Required<Button>(root, $"{prefix}-{i}");
                buttons[i].text = labelFor(i);
                buttons[i].clicked += () => onSelect(index);
            }

            return buttons;
        }

        /// <summary>
        /// Looks up a named element of type <typeparamref name="T"/> under <paramref name="root"/>.
        /// </summary>
        /// <exception cref="SaveInspectorException">
        /// When no element named <paramref name="name"/> exists under <paramref name="root"/>.
        /// </exception>
        private static T Required<T>(VisualElement root, string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null) throw SaveInspectorException.MissingElement(typeof(T).Name, name);

            return element;
        }

        private void ToggleExpanded()
        {
            _expanded = !_expanded;
            _chrome.style.display = _expanded ? DisplayStyle.Flex : DisplayStyle.None;
            _toggleButton.style.display = _expanded ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>
        /// Abbreviated label for a storage backend, shown on its segmented button.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "SaveInspectorPanel, binding, keys and truncation".
        /// </remarks>
        private static string ShortNameOf(SaveStorage storage) => storage switch
        {
            SaveStorage.AtomicFile => "Atomic",
            SaveStorage.PlayerPrefs => "Prefs",
            SaveStorage.InMemory => "Memory",
            _ => "File"
        };

        /// <summary>
        /// Abbreviated label for a codec, shown on its segmented button.
        /// </summary>
        private static string ShortNameOf(SaveCodec codec) => codec switch
        {
            SaveCodec.JsonPretty => "Pretty",
            SaveCodec.JsonGzip => "Gzip",
            _ => "Json"
        };

        private void RefreshControlLabels()
        {
            for (int i = 0; i < _storageButtons.Length; i++) _storageButtons[i].EnableInClassList(SelectedClass, i == Controller.StorageIndex);
            for (int i = 0; i < _codecButtons.Length; i++) _codecButtons[i].EnableInClassList(SelectedClass, i == Controller.CodecIndex);
            for (int i = 0; i < _protectionButtons.Length; i++) _protectionButtons[i].EnableInClassList(SelectedClass, i == Controller.ProtectionIndex);
        }

        private void RenderBusy(bool busy)
        {
            _saveButton.SetEnabled(!busy);
            _tamperButton.SetEnabled(!busy && Controller.HasSaved);
        }

        private void OnSaveClicked()
        {
            if (!IsBound) return;
            Controller.SaveAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private void OnTamperClicked()
        {
            if (!IsBound) return;
            Controller.TamperAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private void ShowSaveResult(SaveProbeResult result, SaveProbeResult baseline)
        {
            double percentOfBaseline = baseline.ByteCount == 0 ? 0d : 100d * result.ByteCount / baseline.ByteCount;

            _readoutLabel.text =
                $"{ComboText(Controller.SavedStorage, Controller.SavedCodec, Controller.SavedProtection)}\n" +
                $"{result.ByteCount} bytes ({percentOfBaseline:F0}% of the {baseline.ByteCount}-byte plaintext baseline)";
            _timingsLabel.text = $"write {result.WriteMilliseconds:F2} ms  ·  read {result.ReadMilliseconds:F2} ms";

            SetBytesText(result);
        }

        private void ShowSaveFailure(SaveException failure)
        {
            _readoutLabel.text = $"Save failed: {failure.Message}";
            _timingsLabel.text = "write - · read -";
        }

        /// <summary>
        /// Shows the rendered save bytes, truncated past <see cref="MaxRenderedCharacters"/>.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "The bytes are always renderable, and that is structural".
        /// </remarks>
        private void SetBytesText(SaveProbeResult result)
        {
            string text = result.RenderedText;
            if (string.IsNullOrEmpty(text))
            {
                _bytesLabel.text = "(empty)";
                return;
            }

            _bytesLabel.text = text.Length > MaxRenderedCharacters
                ? text.Substring(0, MaxRenderedCharacters) + $"\n\n... truncated, showing {MaxRenderedCharacters} of {result.ByteCount} bytes."
                : text;
        }

        private void ShowIdleReadout()
        {
            _readoutLabel.text = "Pick a combination, then press Save to run it through the real pipeline.";
            _timingsLabel.text = "write - · read -";
            _bytesLabel.text = "(nothing saved yet)";
        }

        /// <summary>
        /// Renders the tamper outcome, styling accepted and refused edits so they do not look alike.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "The tamper button, and why it edits two different ways".
        /// </remarks>
        private void ShowTamperResult(SaveTamperResult result)
        {
            bool accepted = result.Outcome == SaveTamperOutcome.Loaded;
            _tamperReadout.EnableInClassList(TamperAcceptedClass, accepted);
            _tamperReadout.EnableInClassList(TamperRejectedClass, !accepted);

            string combo = ComboText(Controller.SavedStorage, Controller.SavedCodec, Controller.SavedProtection);
            _tamperLabel.text = result.Outcome switch
            {
                SaveTamperOutcome.Loaded =>
                    $"{combo}\nACCEPTED - reloaded with the tampered balance ({result.LoadedBalance}). This pipeline does not detect the edit.",
                SaveTamperOutcome.RejectedAsTampered =>
                    $"{combo}\nREJECTED as tampered - {result.Error.Message}",
                _ =>
                    $"{combo}\nREJECTED as unreadable - {result.Error.Message}"
            };
        }

        private void ShowTamperFailure(SaveInspectorException failure)
        {
            _tamperLabel.text = failure.Message;
        }

        private void ShowIdleTamperReadout()
        {
            _tamperReadout.RemoveFromClassList(TamperAcceptedClass);
            _tamperReadout.RemoveFromClassList(TamperRejectedClass);
            _tamperLabel.text = "Press Save, then Tamper, to see whether an edited save is caught.";
        }

        private static string ComboText(SaveStorage storage, SaveCodec codec, SaveProtection protection) =>
            $"{storage} / {codec} / {protection}";
    }
}
