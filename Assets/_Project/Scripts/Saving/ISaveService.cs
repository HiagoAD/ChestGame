using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Saving
{
    /// <summary>Saves and loads keyed state.</summary>
    public interface ISaveService
    {
        /// <summary>
        /// Two outcomes, never a third: returns a fresh <typeparamref name="T"/> when nothing is
        /// stored under <paramref name="key"/>. Never returns null.
        /// </summary>
        /// <exception cref="SaveException">When something is stored under <paramref name="key"/>
        /// and cannot be read.</exception>
        UniTask<T> LoadAsync<T>(string key, CancellationToken ct) where T : class, new();

        /// <summary>
        /// Once this completes, a later <see cref="LoadAsync{T}"/> returns what was written. File
        /// and PlayerPrefs stores keep it across a process restart; an in-memory store keeps it only
        /// until the process ends.
        /// </summary>
        UniTask SaveAsync<T>(string key, T state, CancellationToken ct) where T : class;

        /// <summary>
        /// True when anything is stored under <paramref name="key"/>, readable or not, so true does
        /// not promise that <see cref="LoadAsync{T}"/> will succeed.
        /// </summary>
        UniTask<bool> ExistsAsync(string key, CancellationToken ct);

        /// <summary>
        /// Removes whatever is stored under <paramref name="key"/>. Does nothing when nothing is
        /// stored.
        /// </summary>
        UniTask DeleteAsync(string key, CancellationToken ct);

        /// <summary>
        /// True when every call above finishes on the thread that started it. Only then is it safe
        /// to block on one of these tasks for its result; blocking when this is false risks
        /// deadlock.
        /// </summary>
        bool CompletesOnCallingThread { get; }
    }
}
