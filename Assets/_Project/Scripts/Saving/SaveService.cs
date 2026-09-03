using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Saving
{
    // Composes a codec, a protector and a store into ISaveService. See docs/saving.md.
    public class SaveService : ISaveService
    {
        // Phase 4's migration chain hooks in at the version checks below.
        public const int CurrentSchemaVersion = 1;

        private static readonly UTF8Encoding Utf8 = new(false);

        private readonly ISaveCodec _codec;
        private readonly IPayloadProtector _protector;
        private readonly ISaveStore _store;

        public SaveService(ISaveCodec codec, IPayloadProtector protector, ISaveStore store)
        {
            _codec = codec;
            _protector = protector;
            _store = store;
        }

        public async UniTask<T> LoadAsync<T>(string key, CancellationToken ct) where T : class, new()
        {
            byte[] bytes = await _store.ReadAsync(key, ct);
            if (bytes == null) return new T();

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
            if (version < CurrentSchemaVersion) throw SaveException.NoMigrationPath(key, version, CurrentSchemaVersion);

            // After the version check: a newer build's save may name a codec this one has never
            // heard of, and that is the more useful failure to report.
            if (envelope.CodecId != _codec.Id) throw SaveException.UnexpectedComponent(key, "codec", _codec.Id, envelope.CodecId);
            if (envelope.ProtectorId != _protector.Id) throw SaveException.UnexpectedComponent(key, "protector", _protector.Id, envelope.ProtectorId);

            try
            {
                byte[] body = envelope.GetBody();
                if (body == null) throw SaveException.PayloadMissing(key);

                body = _protector.Unprotect(body);

                // A codec can decode without throwing and still hand back null — GzipJsonCodec on
                // a truncated stream decompresses to zero bytes, and JsonConvert.DeserializeObject
                // of an empty string returns null rather than failing. Null is neither of the two
                // outcomes this contract allows: not a fresh T, because something was stored, and
                // not a value, because the payload never became one. Refusing it here keeps that
                // third state from surfacing as a NullReferenceException far from the save that
                // caused it, and covers every codec rather than only the one that exposed it.
                T value = _codec.Decode<T>(body);
                if (value == null) throw SaveException.PayloadUnreadable(key, null);

                return value;
            }
            catch (SaveException)
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
