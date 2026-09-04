using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Company.ChestGame.Saving
{
    // Composes a codec, a protector and a store into ISaveService. See docs/saving.md.
    public class SaveService : ISaveService
    {
        // There is no v1 -> v2 migration to write until a save model exists, so this stays at 1
        // rather than inventing one to prove the chain below - the corpus and test-only migrations
        // prove it instead. See docs/saving.md, "The migration chain".
        public const int CurrentSchemaVersion = 1;

        private static readonly UTF8Encoding Utf8 = new(false);

        private readonly ISaveCodec _codec;
        private readonly IPayloadProtector _protector;
        private readonly ISaveStore _store;
        private readonly SaveMigrator _migrator;
        private readonly ILegacyImport _legacyImport;

        // migrator and legacyImport are both optional so every existing call site keeps compiling
        // and behaving exactly as before: no migrator means an older-than-current save still fails
        // as NoMigrationPath, and no legacy import means a missing save still just reads as a first
        // run.
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

            // Before the two comparisons, never folded into them: a null Version answers false to
            // both > and <, so a file with no "v" would reach the codec instead of being refused.
            if (!envelope.Version.HasValue) throw SaveException.PayloadUnreadable(key, null);

            int version = envelope.Version.Value;

            if (version > CurrentSchemaVersion) throw SaveException.VersionTooNew(key, version, CurrentSchemaVersion);

            // No migrator: exactly the failure this threw unconditionally before the chain existed.
            // A migrator turns this from a dead end into the branch taken further down.
            if (version < CurrentSchemaVersion && _migrator == null) throw SaveException.NoMigrationPath(key, version, CurrentSchemaVersion);

            // After the version check: a newer build's save may name a codec this one has never
            // heard of, and that is the more useful failure to report.
            if (envelope.CodecId != _codec.Id) throw SaveException.UnexpectedComponent(key, "codec", _codec.Id, envelope.CodecId);
            if (envelope.ProtectorId != _protector.Id) throw SaveException.UnexpectedComponent(key, "protector", _protector.Id, envelope.ProtectorId);

            try
            {
                byte[] body = envelope.GetBody();
                if (body == null) throw SaveException.PayloadMissing(key);

                body = _protector.Unprotect(body);

                // Equal-version reads through Decode<T> exactly as before phase 4. Older-than-current
                // (only reachable with a migrator, given the guard above) goes through the codec's
                // own JSON instead, walks the chain, and materialises T from the migrated document
                // directly - both routes are Newtonsoft over the same document, just Decode<T> over
                // the codec's bytes versus JObject.ToObject<T> over the chain's output, so neither is
                // more "real" than the other. See docs/saving.md, "The migration chain".
                T value = version == CurrentSchemaVersion
                    ? _codec.Decode<T>(body)
                    : _migrator.Migrate(key, JObject.Parse(_codec.ToJson(body)), version, CurrentSchemaVersion).ToObject<T>();

                // A codec can decode without throwing and still hand back null — GzipJsonCodec on
                // a truncated stream decompresses to zero bytes, and JsonConvert.DeserializeObject
                // of an empty string returns null rather than failing. Null is neither of the two
                // outcomes this contract allows: not a fresh T, because something was stored, and
                // not a value, because the payload never became one. Refusing it here keeps that
                // third state from surfacing as a NullReferenceException far from the save that
                // caused it, and covers every codec rather than only the one that exposed it.
                if (value == null) throw SaveException.PayloadUnreadable(key, null);

                return value;
            }
            catch (SaveException)
            {
                throw;
            }
            catch (SaveMigrationException)
            {
                // A wiring mistake, not a delivery failure: rethrown unchanged rather than folded
                // into PayloadUnreadable below, the same reasoning that keeps SaveException's own
                // catch above it.
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

        // Runs before any envelope exists at all - not merely before the migration chain, since a
        // legacy save has no version field for the chain to start from. Only reachable when the
        // configured store has nothing under key: once the import below writes a real save, this
        // store answers non-null on every later call and this method is never reached again for
        // that key, which is what makes the whole thing idempotent without IsPresent() needing to be
        // perfectly accurate after a partial run.
        private async UniTask<T> ImportLegacyOrFreshAsync<T>(string key, CancellationToken ct) where T : class, new()
        {
            if (_legacyImport == null || !_legacyImport.IsPresent()) return new T();

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

            // Written and durably persisted before the legacy data is touched: if this throws, or if
            // the process dies before Clear() below ever runs, the legacy data is exactly as it was
            // and the next LoadAsync call finds it again. If the process dies after this succeeds but
            // before Clear() runs, the next call finds a real save under key and never reaches this
            // method at all - the legacy data is simply orphaned, never re-applied. See
            // docs/saving.md, "The legacy import".
            await SaveAsync(key, imported, ct);

            try
            {
                _legacyImport.Clear();
            }
            catch
            {
                // Best-effort, the same reasoning AtomicFileStore's own temp-file cleanup follows:
                // the new save already succeeded, so a legacy entry that fails to clear is an inert
                // leftover, not a lost one, and must not turn a successful load into a failure.
            }

            return imported;
        }

        public async UniTask SaveAsync<T>(string key, T state, CancellationToken ct) where T : class
        {
            // Before encoding: the store checks too, but by then the whole graph is serialised.
            ct.ThrowIfCancellationRequested();

            byte[] plain = _codec.Encode(state);
            byte[] protectedBytes = _protector.Protect(plain);

            bool textSafe = _codec.IsTextSafe && _protector.IsTextSafe;
            SaveEnvelope envelope = SaveEnvelope.Wrap(CurrentSchemaVersion, _codec.Id, _protector.Id, textSafe, protectedBytes);

            await _store.WriteAsync(key, Utf8.GetBytes(envelope.Serialize()), ct);
        }

        public UniTask<bool> ExistsAsync(string key, CancellationToken ct) => _store.ExistsAsync(key, ct);

        public UniTask DeleteAsync(string key, CancellationToken ct) => _store.DeleteAsync(key, ct);
    }
}
