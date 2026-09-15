using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Saving;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// An in-memory <see cref="ISaveStore"/> test double.
    /// </summary>
    /// <remarks>
    /// See docs/testing.md, "The save fixtures".
    /// </remarks>
    public class FakeSaveStore : ISaveStore
    {
        private readonly Dictionary<string, byte[]> _files = new();

        /// <summary>
        /// Stores <paramref name="bytes"/> directly under <paramref name="key"/>, bypassing
        /// <see cref="WriteAsync"/> and the codec/protector pipeline it would normally go through.
        /// </summary>
        /// <remarks>
        /// Use this to place an envelope on "disk" that the pipeline could never have produced
        /// itself: a corrupt one, one from a different schema version, or one naming a different
        /// codec or protector.
        /// </remarks>
        public void Seed(string key, byte[] bytes) => _files[key] = bytes;

        public UniTask WriteAsync(string key, byte[] bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _files[key] = bytes;
            return UniTask.CompletedTask;
        }

        public UniTask<byte[]> ReadAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return UniTask.FromResult(_files.TryGetValue(key, out byte[] bytes) ? bytes : null);
        }

        public UniTask<bool> ExistsAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return UniTask.FromResult(_files.ContainsKey(key));
        }

        public UniTask DeleteAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _files.Remove(key);
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// Always <c>true</c>: every operation above is a dictionary access wrapped in an
        /// already-completed <see cref="UniTask"/>, so none of them ever suspends.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "The thread hop, and why it is not inside SaveService".
        /// </remarks>
        public bool CompletesOnCallingThread => true;
    }
}
