using System;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Mvc;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Saving.Demo
{
    /// <summary>
    /// The save inspector's rules: the storage/codec/protection selection, the save probe and tamper
    /// flows built over <see cref="SavePipelineProbe"/> and <see cref="SaveTamper"/>, and what each
    /// last did.
    /// </summary>
    /// <remarks>
    /// See docs/mvc.md. See docs/saving.md, "The save inspector".
    /// </remarks>
    public interface ISaveInspectorController : IController
    {
        IReadOnlyList<SaveStorage> Storages { get; }
        IReadOnlyList<SaveCodec> Codecs { get; }
        IReadOnlyList<SaveProtection> Protections { get; }

        int StorageIndex { get; }
        int CodecIndex { get; }
        int ProtectionIndex { get; }

        /// <summary>Whether a save or tamper is running. Gates re-entry into either.</summary>
        bool IsBusy { get; }

        /// <summary>Whether <see cref="SaveAsync"/> has ever completed successfully.</summary>
        bool HasSaved { get; }

        /// <summary>The combination <see cref="SaveAsync"/> last saved under. Only meaningful when <see cref="HasSaved"/> is true.</summary>
        SaveStorage SavedStorage { get; }

        /// <summary>The combination <see cref="SaveAsync"/> last saved under. Only meaningful when <see cref="HasSaved"/> is true.</summary>
        SaveCodec SavedCodec { get; }

        /// <summary>The combination <see cref="SaveAsync"/> last saved under. Only meaningful when <see cref="HasSaved"/> is true.</summary>
        SaveProtection SavedProtection { get; }

        /// <summary>Raised after <see cref="StorageIndex"/>, <see cref="CodecIndex"/> or <see cref="ProtectionIndex"/> changes.</summary>
        event Action OnSelectionChanged;

        /// <summary>Raised with the new <see cref="IsBusy"/> whenever a save or tamper starts or settles.</summary>
        event Action<bool> OnBusyChanged;

        /// <summary>Raised when <see cref="SaveAsync"/> completes successfully, with its result and the plaintext baseline it was measured against.</summary>
        event Action<SaveProbeResult, SaveProbeResult> OnSaveCompleted;

        /// <summary>Raised when <see cref="SaveAsync"/> fails.</summary>
        event Action<SaveException> OnSaveFailed;

        /// <summary>Raised when <see cref="TamperAsync"/> completes, whether the tampered save was accepted or rejected.</summary>
        event Action<SaveTamperResult> OnTamperCompleted;

        /// <summary>Raised when <see cref="TamperAsync"/> fails outright, rather than settling with a rejected outcome.</summary>
        event Action<SaveInspectorException> OnTamperFailed;

        void SetStorage(int index);
        void SetCodec(int index);
        void SetProtection(int index);

        /// <summary>
        /// Runs the plaintext baseline and the selected combination through <see cref="SavePipelineProbe"/>
        /// and records what was saved. A no-op while <see cref="IsBusy"/> is true.
        /// </summary>
        UniTask SaveAsync(CancellationToken ct);

        /// <summary>
        /// Edits the save <see cref="SaveAsync"/> last wrote and reloads it through
        /// <see cref="SaveTamper"/>. A no-op while <see cref="IsBusy"/> is true or before
        /// <see cref="HasSaved"/> is true.
        /// </summary>
        UniTask TamperAsync(CancellationToken ct);
    }
}
