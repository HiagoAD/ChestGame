using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Saving.Demo
{
    /// <summary>
    /// Drives the save inspector demo: holds the storage/codec/protection selection and runs the
    /// save probe and tamper flows over it.
    /// </summary>
    /// <remarks>
    /// See docs/mvc.md. See docs/saving.md, "The save inspector".
    /// </remarks>
    public sealed class SaveInspectorController : ISaveInspectorController
    {
        /// <summary>Storage key every save and tamper this controller runs uses.</summary>
        public const string Key = "save-inspector-demo";

        /// <summary>
        /// Storage key the plaintext-baseline computation writes under, kept separate from <see cref="Key"/>
        /// so it cannot overwrite what <see cref="SaveAsync"/> wrote.
        /// </summary>
        public const string BaselineKey = "save-inspector-demo-baseline";

        /// <summary>The balance <see cref="TamperAsync"/> rewrites the stored save to.</summary>
        public const long TamperedBalance = 999999;

        private static readonly SaveStorage[] StorageValues = (SaveStorage[])Enum.GetValues(typeof(SaveStorage));
        private static readonly SaveCodec[] CodecValues = (SaveCodec[])Enum.GetValues(typeof(SaveCodec));
        private static readonly SaveProtection[] ProtectionValues = (SaveProtection[])Enum.GetValues(typeof(SaveProtection));

        private readonly SaveFactoryInputs _inputs;
        private readonly SaveInspectorDocument _sample;

        private int _storageIndex;
        private int _codecIndex;
        private int _protectionIndex;

        private bool _busy;
        private bool _hasSaved;
        private SaveStorage _savedStorage;
        private SaveCodec _savedCodec;
        private SaveProtection _savedProtection;

        public IReadOnlyList<SaveStorage> Storages => StorageValues;
        public IReadOnlyList<SaveCodec> Codecs => CodecValues;
        public IReadOnlyList<SaveProtection> Protections => ProtectionValues;

        public int StorageIndex => _storageIndex;
        public int CodecIndex => _codecIndex;
        public int ProtectionIndex => _protectionIndex;

        public bool IsBusy => _busy;
        public bool HasSaved => _hasSaved;
        public SaveStorage SavedStorage => _savedStorage;
        public SaveCodec SavedCodec => _savedCodec;
        public SaveProtection SavedProtection => _savedProtection;

        public event Action OnSelectionChanged;
        public event Action<bool> OnBusyChanged;
        public event Action<SaveProbeResult, SaveProbeResult> OnSaveCompleted;
        public event Action<SaveException> OnSaveFailed;
        public event Action<SaveTamperResult> OnTamperCompleted;
        public event Action<SaveInspectorException> OnTamperFailed;

        public SaveInspectorController() : this(SaveFactoryInputs.Defaults(), new SaveInspectorDocument())
        {
        }

        /// <param name="inputs">Backing for whichever storage or protector the selection names.</param>
        /// <param name="sample">The document saved and, after a tamper, reloaded.</param>
        public SaveInspectorController(SaveFactoryInputs inputs, SaveInspectorDocument sample)
        {
            _inputs = inputs;
            _sample = sample;
        }

        public void SetStorage(int index)
        {
            _storageIndex = index;
            OnSelectionChanged?.Invoke();
        }

        public void SetCodec(int index)
        {
            _codecIndex = index;
            OnSelectionChanged?.Invoke();
        }

        public void SetProtection(int index)
        {
            _protectionIndex = index;
            OnSelectionChanged?.Invoke();
        }

        public async UniTask SaveAsync(CancellationToken ct)
        {
            if (_busy) return;
            SetBusy(true);

            SaveStorage storage = StorageValues[_storageIndex];
            SaveCodec codec = CodecValues[_codecIndex];
            SaveProtection protection = ProtectionValues[_protectionIndex];

            try
            {
                SaveProbeResult baseline = await SavePipelineProbe.RunBaselineAsync(storage, _inputs, BaselineKey, _sample, ct);
                SaveProbeResult result = await SavePipelineProbe.RunAsync(storage, codec, protection, _inputs, Key, _sample, ct);

                _savedStorage = storage;
                _savedCodec = codec;
                _savedProtection = protection;
                _hasSaved = true;

                OnSaveCompleted?.Invoke(result, baseline);
            }
            catch (OperationCanceledException)
            {
            }
            catch (SaveException failure)
            {
                Debug.LogException(failure);
                OnSaveFailed?.Invoke(failure);
            }
            finally
            {
                SetBusy(false);
            }
        }

        public async UniTask TamperAsync(CancellationToken ct)
        {
            if (_busy || !_hasSaved) return;
            SetBusy(true);

            SaveStorage storage = _savedStorage;
            SaveCodec codec = _savedCodec;
            SaveProtection protection = _savedProtection;

            try
            {
                SaveTamperResult result = await SaveTamper.RunAsync(storage, codec, protection, _inputs, Key, TamperedBalance, ct);
                OnTamperCompleted?.Invoke(result);
            }
            catch (OperationCanceledException)
            {
            }
            catch (SaveInspectorException failure)
            {
                Debug.LogException(failure);
                OnTamperFailed?.Invoke(failure);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            OnBusyChanged?.Invoke(busy);
        }

        public void Dispose()
        {
            OnSelectionChanged = null;
            OnBusyChanged = null;
            OnSaveCompleted = null;
            OnSaveFailed = null;
            OnTamperCompleted = null;
            OnTamperFailed = null;
        }
    }
}
