using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Company.ChestGame.Saving.Demo
{
    // The UI half of the phase 8a probe/tamper pair: authored chrome in SaveInspector.uxml, this
    // class only binds to it - query by name, set text, toggle a class. See
    // Company.ChestGame.Pooling.Demo.PoolingDemoPanel, which this mirrors, and docs/saving.md, "The
    // save inspector".
    public sealed class SaveInspectorPanel : MonoBehaviour
    {
        private const string SelectedClass = "is-selected";
        private const string TamperAcceptedClass = "tamper-readout--accepted";
        private const string TamperRejectedClass = "tamper-readout--rejected";

        // One fixed key per role, reused across every combination - only the currently selected
        // storage/codec/protector changes what lands under it. A distinct baseline key keeps
        // RunBaselineAsync's own write off the key Save/Tamper operate on.
        private const string Key = "save-inspector-demo";
        private const string BaselineKey = "save-inspector-demo-baseline";

        private const long TamperedBalance = 999999;

        // A payload this small never gets close to this in the shipped demo document; it exists so
        // a much larger one still leaves the layout intact rather than proving anything about
        // today's fixture.
        private const int MaxRenderedCharacters = 4000;

        // Declaration order, not display order picked here - a reordering of the enum (against its
        // own append-only rule) still labels every button from the value it actually selects.
        private static readonly SaveStorage[] Storages = (SaveStorage[])Enum.GetValues(typeof(SaveStorage));
        private static readonly SaveCodec[] Codecs = (SaveCodec[])Enum.GetValues(typeof(SaveCodec));
        private static readonly SaveProtection[] Protections = (SaveProtection[])Enum.GetValues(typeof(SaveProtection));

        [Header("Authored chrome")]
        [SerializeField] private UIDocument _document;

        private VisualElement _chrome;
        private Button _toggleButton;
        private bool _expanded;

        private Button[] _storageButtons;
        private Button[] _codecButtons;
        private Button[] _protectionButtons;
        private int _storageIndex;
        private int _codecIndex;
        private int _protectionIndex;

        private Button _saveButton;
        private Button _tamperButton;
        private Label _readoutLabel;
        private Label _timingsLabel;
        private Label _bytesLabel;
        private VisualElement _tamperReadout;
        private Label _tamperLabel;

        private SaveFactoryInputs _inputs;
        private SaveInspectorDocument _sample;

        private bool _busy;
        private bool _hasSaved;
        private SaveStorage _savedStorage;
        private SaveCodec _savedCodec;
        private SaveProtection _savedProtection;

        // Start, not Awake: UIDocument builds rootVisualElement in OnEnable.
        private void Start()
        {
            if (_document == null) throw SaveInspectorException.NoDocument();

            _inputs = SaveFactoryInputs.Defaults();
            _sample = new SaveInspectorDocument();

            BindChrome();

            _expanded = true;
            ToggleExpanded();
        }

        // --- Binding to the authored tree --------------------------------------------------------

        private void BindChrome()
        {
            VisualElement root = _document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;

            _chrome = Required<VisualElement>(root, "chrome");
            _toggleButton = Required<Button>(root, "toggle-button");
            _readoutLabel = Required<Label>(root, "readout-label");
            _timingsLabel = Required<Label>(root, "timings-label");
            _bytesLabel = Required<Label>(root, "bytes-label");
            _tamperReadout = Required<VisualElement>(root, "tamper-readout");
            _tamperLabel = Required<Label>(root, "tamper-label");

            _toggleButton.clicked += ToggleExpanded;
            Required<Button>(root, "close-button").clicked += ToggleExpanded;

            _saveButton = Required<Button>(root, "save-button");
            _saveButton.clicked += () => SaveAsync().Forget();

            _tamperButton = Required<Button>(root, "tamper-button");
            _tamperButton.clicked += () => TamperAsync().Forget();
            _tamperButton.SetEnabled(false);

            _storageButtons = BindSegment(root, "storage", Storages.Length, i => ShortNameOf(Storages[i]), SetStorage);
            _codecButtons = BindSegment(root, "codec", Codecs.Length, i => ShortNameOf(Codecs[i]), SetCodec);
            _protectionButtons = BindSegment(root, "protection", Protections.Length, i => Protections[i].ToString(), SetProtection);

            RefreshControlLabels();
            ShowIdleReadout();
            ShowIdleTamperReadout();
        }

        // One segmented group, built the same way for all three axes: label each button from the
        // enum rather than trusting whatever SaveInspector.uxml happens to say at that index.
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

        // A missing name is a broken .uxml, not a state to limp along in.
        private static T Required<T>(VisualElement root, string name) where T : VisualElement
        {
            T element = root.Q<T>(name);
            if (element == null) throw SaveInspectorException.MissingElement(typeof(T).Name, name);

            return element;
        }

        // --- Collapsing and expanding -------------------------------------------------------------

        private void ToggleExpanded()
        {
            _expanded = !_expanded;
            _chrome.style.display = _expanded ? DisplayStyle.Flex : DisplayStyle.None;
            _toggleButton.style.display = _expanded ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // --- The segmented controls' labels --------------------------------------------------------

        // Written out because the four enum names run wider than a segment on a narrow phone; the
        // full name still leads the combo readout once a result comes back.
        private static string ShortNameOf(SaveStorage storage) => storage switch
        {
            SaveStorage.AtomicFile => "Atomic",
            SaveStorage.PlayerPrefs => "Prefs",
            SaveStorage.InMemory => "Memory",
            _ => "File"
        };

        private static string ShortNameOf(SaveCodec codec) => codec switch
        {
            SaveCodec.JsonPretty => "Pretty",
            SaveCodec.JsonGzip => "Gzip",
            _ => "Json"
        };

        // --- Control callbacks ----------------------------------------------------------------------

        private void SetStorage(int index)
        {
            _storageIndex = index;
            RefreshControlLabels();
        }

        private void SetCodec(int index)
        {
            _codecIndex = index;
            RefreshControlLabels();
        }

        private void SetProtection(int index)
        {
            _protectionIndex = index;
            RefreshControlLabels();
        }

        // Selection is a class the stylesheet reacts to, not a colour set from here.
        private void RefreshControlLabels()
        {
            for (int i = 0; i < _storageButtons.Length; i++) _storageButtons[i].EnableInClassList(SelectedClass, i == _storageIndex);
            for (int i = 0; i < _codecButtons.Length; i++) _codecButtons[i].EnableInClassList(SelectedClass, i == _codecIndex);
            for (int i = 0; i < _protectionButtons.Length; i++) _protectionButtons[i].EnableInClassList(SelectedClass, i == _protectionIndex);
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _saveButton.SetEnabled(!busy);
            _tamperButton.SetEnabled(!busy && _hasSaved);
        }

        // --- Save -------------------------------------------------------------------------------

        private async UniTaskVoid SaveAsync()
        {
            if (_busy) return;
            SetBusy(true);

            SaveStorage storage = Storages[_storageIndex];
            SaveCodec codec = Codecs[_codecIndex];
            SaveProtection protection = Protections[_protectionIndex];
            CancellationToken ct = this.GetCancellationTokenOnDestroy();

            try
            {
                SaveProbeResult baseline = await SavePipelineProbe.RunBaselineAsync(storage, _inputs, BaselineKey, _sample, ct);
                SaveProbeResult result = await SavePipelineProbe.RunAsync(storage, codec, protection, _inputs, Key, _sample, ct);

                _savedStorage = storage;
                _savedCodec = codec;
                _savedProtection = protection;
                _hasSaved = true;

                ShowSaveResult(storage, codec, protection, result, baseline);
            }
            catch (OperationCanceledException)
            {
                // Torn down mid-save; nothing left to show it to.
            }
            catch (SaveException failure)
            {
                Debug.LogException(failure);
                _readoutLabel.text = $"Save failed: {failure.Message}";
                _timingsLabel.text = "write - · read -";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void ShowSaveResult(SaveStorage storage, SaveCodec codec, SaveProtection protection, SaveProbeResult result, SaveProbeResult baseline)
        {
            double percentOfBaseline = baseline.ByteCount == 0 ? 0d : 100d * result.ByteCount / baseline.ByteCount;

            _readoutLabel.text =
                $"{ComboText(storage, codec, protection)}\n" +
                $"{result.ByteCount} bytes ({percentOfBaseline:F0}% of the {baseline.ByteCount}-byte plaintext baseline)";
            _timingsLabel.text = $"write {result.WriteMilliseconds:F2} ms  ·  read {result.ReadMilliseconds:F2} ms";

            SetBytesText(result);
        }

        // Every combination this factory can build stores valid UTF-8 - see
        // SavePipelineProbe.Render - so RenderedText is what actually shows here; IsHexDump only
        // ever fires against bytes nothing shipped today produces. Truncated rather than rendered in
        // full past a stated size, so one long payload cannot break the layout around it.
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

        // --- Tamper -----------------------------------------------------------------------------

        private async UniTaskVoid TamperAsync()
        {
            if (_busy || !_hasSaved) return;
            SetBusy(true);

            // The combination Save last wrote under, not whatever the selector currently shows - a
            // tamper against a combination nothing was ever saved under would just fail to parse,
            // which is not the demonstration this button exists for.
            SaveStorage storage = _savedStorage;
            SaveCodec codec = _savedCodec;
            SaveProtection protection = _savedProtection;
            CancellationToken ct = this.GetCancellationTokenOnDestroy();

            try
            {
                SaveTamperResult result = await SaveTamper.RunAsync(storage, codec, protection, _inputs, Key, TamperedBalance, ct);
                ShowTamperResult(storage, codec, protection, result);
            }
            catch (OperationCanceledException)
            {
                // Torn down mid-tamper.
            }
            catch (SaveInspectorException failure)
            {
                // Guarded by _hasSaved above; only reachable if something else already cleared Key.
                Debug.LogException(failure);
                _tamperLabel.text = failure.Message;
            }
            finally
            {
                SetBusy(false);
            }
        }

        // The point of the whole panel: an accepted edit and a refused one must not look alike.
        // Carried by a class the stylesheet reacts to, never a colour set from here.
        private void ShowTamperResult(SaveStorage storage, SaveCodec codec, SaveProtection protection, SaveTamperResult result)
        {
            bool accepted = result.Outcome == SaveTamperOutcome.Loaded;
            _tamperReadout.EnableInClassList(TamperAcceptedClass, accepted);
            _tamperReadout.EnableInClassList(TamperRejectedClass, !accepted);

            string combo = ComboText(storage, codec, protection);
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
