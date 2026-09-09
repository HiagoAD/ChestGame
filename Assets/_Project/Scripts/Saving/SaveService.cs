using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Company.ChestGame.Saving
{
    // Composes a codec, a protector and a store into a working ISaveService. Every save it writes
    // carries a plaintext header naming the version and the two components used, so a later read
    // can refuse a save it cannot correctly decode instead of guessing. See docs/saving.md for the
    // reasoning behind that arrangement.
    public class SaveService : ISaveService
    {
        // Stamped into every save this writes. Raise it when the stored shape changes, and supply a
        // migration for each step, or older saves become unreadable.
        public const int CurrentSchemaVersion = 1;

        private static readonly UTF8Encoding Utf8 = new(false);

        private readonly ISaveCodec _codec;
        private readonly IPayloadProtector _protector;
        private readonly ISaveStore _store;
        private readonly SaveMigrator _migrator;
        private readonly ILegacyImport _legacyImport;

        // Both optional. Without a migrator, a save older than CurrentSchemaVersion fails rather
        // than being upgraded. Without a legacy import, a key with nothing stored simply reads as a
        // first run.
        public SaveService(ISaveCodec codec, IPayloadProtector protector, ISaveStore store,
            SaveMigrator migrator = null, ILegacyImport legacyImport = null)
        {
            _codec = codec;
            _protector = protector;
            _store = store;
            _migrator = migrator;
            _legacyImport = legacyImport;
        }

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

            // Checked separately, never folded into the comparisons below: a missing version
            // answers false to both > and <, so it would otherwise reach the codec unrefused.
            if (!envelope.Version.HasValue) throw SaveException.PayloadUnreadable(key, null);

            int version = envelope.Version.Value;

            if (version > CurrentSchemaVersion) throw SaveException.VersionTooNew(key, version, CurrentSchemaVersion);

            // Without a migrator there is nowhere for an older save to go.
            if (version < CurrentSchemaVersion && _migrator == null) throw SaveException.NoMigrationPath(key, version, CurrentSchemaVersion);

            // After the version check, so a save from a newer build reports the version rather than
            // an unrecognised component name.
            if (envelope.CodecId != _codec.Id) throw SaveException.UnexpectedComponent(key, "codec", _codec.Id, envelope.CodecId);
            if (envelope.ProtectorId != _protector.Id) throw SaveException.UnexpectedComponent(key, "protector", _protector.Id, envelope.ProtectorId);

            try
            {
                byte[] body = envelope.GetBody();
                if (body == null) throw SaveException.PayloadMissing(key);

                body = _protector.Unprotect(body);

                // The older-than-current branch is only reachable with a migrator, given the guard
                // above.
                T value = version == CurrentSchemaVersion
                    ? _codec.Decode<T>(body)
                    : _migrator.Migrate(key, JObject.Parse(_codec.ToJson(body)), version, CurrentSchemaVersion).ToObject<T>();

                // A codec can decode without throwing and still hand back null - a truncated
                // stream that decompresses to nothing does exactly this. Null is neither outcome
                // this method promises, so it is refused here rather than surfacing as a
                // NullReferenceException far from the save that caused it.
                if (value == null) throw SaveException.PayloadUnreadable(key, null);

                return value;
            }
            catch (SaveException)
            {
                throw;
            }
            catch (SaveMigrationException)
            {
                // A wiring mistake rather than an unreadable save, so it is not folded into the
                // failure below.
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

        // Only reachable when nothing is stored under key. Once the import below writes a real
        // save, this is never reached again for that key, so it cannot run twice even if the import
        // misreports whether its data is still there.
        private async UniTask<T> ImportLegacyOrFreshAsync<T>(string key, CancellationToken ct) where T : class, new()
        {
            // Checked before the import is asked anything at all: one wired for a different key
            // must not even be consulted, let alone have its data read or cleared.
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

            // Durable before the legacy data is touched, so an interruption either side of this
            // line is survivable: before it, the legacy data is still there to find again; after
            // it, a real save exists and this is never reached again.
            await SaveAsync(key, imported, ct);

            try
            {
                _legacyImport.Clear();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Best effort: the new save already succeeded, so a legacy entry left behind is an
                // inert leftover and must not turn a successful load into a failure.
                Debug.LogError($"Failed to clear the legacy save under '{key}' after importing it: {exception.Message}");
            }

            return imported;
        }

        public async UniTask SaveAsync<T>(string key, T state, CancellationToken ct) where T : class
        {
            // Before encoding, so a cancelled save does not serialise the whole graph first.
            ct.ThrowIfCancellationRequested();

            byte[] plain = _codec.Encode(state);
            byte[] protectedBytes = _protector.Protect(plain);

            bool textSafe = _codec.IsTextSafe && _protector.IsTextSafe;
            SaveEnvelope envelope = SaveEnvelope.Wrap(CurrentSchemaVersion, _codec.Id, _protector.Id, textSafe, protectedBytes);

            await _store.WriteAsync(key, Utf8.GetBytes(envelope.Serialize()), ct);
        }

        public UniTask<bool> ExistsAsync(string key, CancellationToken ct) => _store.ExistsAsync(key, ct);

        public UniTask DeleteAsync(string key, CancellationToken ct) => _store.DeleteAsync(key, ct);

        // Answered by the store this was composed with, which is the only thing that knows.
        public bool CompletesOnCallingThread => _store.CompletesOnCallingThread;
    }
}
