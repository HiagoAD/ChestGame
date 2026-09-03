using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Saving
{
    // Bytes in a dictionary rather than on disk - the general form of
    // Tests/Common/InMemoryResourceBankSaveHandler, and a legitimate production choice for an editor
    // mode that must leave the real save alone rather than only a test double.
    public class InMemoryStore : ISaveStore
    {
        private readonly Dictionary<string, byte[]> _bytesByKey = new();

        public UniTask WriteAsync(string key, byte[] bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            SaveKeyPath.EnsurePresent(key);

            // Copied rather than aliased: a caller mutating its own array afterwards must not reach
            // into what this store believes it has saved.
            _bytesByKey[key] = (byte[])(bytes ?? Array.Empty<byte>()).Clone();

            return UniTask.CompletedTask;
        }

        public UniTask<byte[]> ReadAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            SaveKeyPath.EnsurePresent(key);

            byte[] bytes = _bytesByKey.TryGetValue(key, out byte[] stored) ? (byte[])stored.Clone() : null;
            return UniTask.FromResult(bytes);
        }

        public UniTask<bool> ExistsAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            SaveKeyPath.EnsurePresent(key);

            return UniTask.FromResult(_bytesByKey.ContainsKey(key));
        }

        public UniTask DeleteAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            SaveKeyPath.EnsurePresent(key);

            _bytesByKey.Remove(key);

            return UniTask.CompletedTask;
        }
    }
}
