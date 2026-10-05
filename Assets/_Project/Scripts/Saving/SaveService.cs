using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Composes a codec, a protector and a store into a working <see cref="ISaveService"/>. Every
    /// save it writes carries a plaintext header naming the schema version and the codec and
    /// protector ids used to produce it.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The envelope".
    /// </remarks>
    public class SaveService : ISaveService
    {
        /// <summary>
        /// Stamped into every save this writes.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "What adding a schema version takes".
        /// </remarks>
        public const int CurrentSchemaVersion = 1;

        private static readonly UTF8Encoding Utf8 = new(false);

        private readonly ISaveCodec _codec;
        private readonly IPayloadProtector _protector;
        private readonly ISaveStore _store;
        private readonly SaveMigrator _migrator;
        private readonly ILegacyImport _legacyImport;

        /// <param name="migrator">
        /// Optional. Without one, a save older than <see cref="CurrentSchemaVersion"/> fails to load
        /// rather than being upgraded.
        /// </param>
        /// <param name="legacyImport">
        /// Optional. Without one, a key with nothing stored simply reads as a first run.
        /// </param>
        /// <remarks>
        /// See docs/saving.md, "Wiring: what LoadAsync does with a migrator".
        /// See docs/saving.md, "The legacy import".
        /// </remarks>
        public SaveService(ISaveCodec codec, IPayloadProtector protector, ISaveStore store,
            SaveMigrator migrator = null, ILegacyImport legacyImport = null)
        {
            _codec = codec;
            _protector = protector;
            _store = store;
            _migrator = migrator;
            _legacyImport = legacyImport;
        }

        /// <exception cref="SaveException">
        /// When the stored payload cannot be parsed or decoded, its body is missing, or it decodes
        /// to no value; when its version is newer than <see cref="CurrentSchemaVersion"/>; when it
        /// is older and no migrator was supplied; when its codec or protector id does not match the
        /// ones this instance was built with; when protection verification fails; or when importing
        /// legacy data for a key with nothing stored fails to produce one.
        /// </exception>
        /// <exception cref="SaveMigrationException">
        /// When migrating a stored version up to <see cref="CurrentSchemaVersion"/> fails.
        /// </exception>
        /// <remarks>
        /// See docs/saving.md, "Loading, and the three ways a version goes wrong".
        /// See docs/saving.md, "ISaveMigration and SaveMigrator".
        /// See docs/saving.md, "The legacy import".
        /// </remarks>
        public async UniTask<T> LoadAsync<T>(string key, CancellationToken ct) where T : class, new()
        {
            byte[] bytes = await _store.ReadAsync(key, ct);
            if (bytes == null) return await ImportLegacyOrFreshAsync<T>(key, ct);

            SaveEnvelope envelope;
            try
            {
                envelope = SaveEnvelope.Parse(Utf8.GetString(bytes));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw SaveException.PayloadUnreadable(key, exception);
            }

            if (!envelope.Version.HasValue) throw SaveException.PayloadUnreadable(key, null);

            int version = envelope.Version.Value;

            if (version > CurrentSchemaVersion) throw SaveException.VersionTooNew(key, version, CurrentSchemaVersion);

            if (version < CurrentSchemaVersion && _migrator == null) throw SaveException.NoMigrationPath(key, version, CurrentSchemaVersion);

            if (envelope.CodecId != _codec.Id) throw SaveException.UnexpectedComponent(key, "codec", _codec.Id, envelope.CodecId);
            if (envelope.ProtectorId != _protector.Id) throw SaveException.UnexpectedComponent(key, "protector", _protector.Id, envelope.ProtectorId);

            try
            {
                byte[] body = envelope.GetBody();
                if (body == null) throw SaveException.PayloadMissing(key);

                body = _protector.Unprotect(body);

                T value = version == CurrentSchemaVersion
                    ? _codec.Decode<T>(body)
                    : _migrator.Migrate(key, JObject.Parse(_codec.ToJson(body)), version, CurrentSchemaVersion).ToObject<T>();

                if (value == null) throw SaveException.PayloadUnreadable(key, null);

                return value;
            }
            catch (SaveException)
            {
                throw;
            }
            catch (SaveMigrationException)
            {
                throw;
            }
            catch (PayloadTamperedException)
            {
                throw SaveException.PayloadTampered(key);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw SaveException.PayloadUnreadable(key, exception);
            }
        }

        /// <exception cref="SaveException">
        /// When legacy data exists for <paramref name="key"/> but cannot be imported.
        /// </exception>
        /// <remarks>
        /// See docs/saving.md, "The legacy import".
        /// See docs/saving.md, "TargetKey, and the defect a second save key exposed".
        /// </remarks>
        private async UniTask<T> ImportLegacyOrFreshAsync<T>(string key, CancellationToken ct) where T : class, new()
        {
            if (_legacyImport == null || !string.Equals(_legacyImport.TargetKey, key, StringComparison.Ordinal) || !_legacyImport.IsPresent()) return new T();

            T imported;
            try
            {
                JObject document = _legacyImport.Import();
                imported = document?.ToObject<T>();
                if (imported == null) throw SaveException.PayloadUnreadable(key, null);
            }
            catch (SaveException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                throw SaveException.PayloadUnreadable(key, exception);
            }

            await SaveAsync(key, imported, ct);

            try
            {
                _legacyImport.Clear();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Debug.LogError($"Failed to clear the legacy save under '{key}' after importing it: {exception.Message}");
            }

            return imported;
        }

        /// <exception cref="SaveException">When the store cannot write the bytes.</exception>
        /// <remarks>
        /// See docs/saving.md, "The shape, and what it copies".
        /// </remarks>
        public async UniTask SaveAsync<T>(string key, T state, CancellationToken ct) where T : class
        {
            ct.ThrowIfCancellationRequested();

            byte[] plain = _codec.Encode(state);
            byte[] protectedBytes = _protector.Protect(plain);

            bool textSafe = _codec.IsTextSafe && _protector.IsTextSafe;
            SaveEnvelope envelope = SaveEnvelope.Wrap(CurrentSchemaVersion, _codec.Id, _protector.Id, textSafe, protectedBytes);

            await _store.WriteAsync(key, Utf8.GetBytes(envelope.Serialize()), ct);
        }

        public UniTask<bool> ExistsAsync(string key, CancellationToken ct) => _store.ExistsAsync(key, ct);

        public UniTask DeleteAsync(string key, CancellationToken ct) => _store.DeleteAsync(key, ct);

        /// <remarks>
        /// See docs/saving.md, "The thread hop, and why it is not inside SaveService".
        /// </remarks>
        public bool CompletesOnCallingThread => _store.CompletesOnCallingThread;
    }
}
