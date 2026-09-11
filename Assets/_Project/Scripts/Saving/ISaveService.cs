using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Saving
{
    // Saves and loads keyed state.
    public interface ISaveService
    {
        // Two outcomes, never a third. Returns a fresh T when nothing is stored under key.
        // Throws SaveException when something is stored and cannot be read. Never returns null.
        UniTask<T> LoadAsync<T>(string key, CancellationToken ct) where T : class, new();

        // Once this completes, a later LoadAsync returns what was written. File and PlayerPrefs
        // stores keep it across a process restart; an in-memory store keeps it only until the
        // process ends.
        UniTask SaveAsync<T>(string key, T state, CancellationToken ct) where T : class;

        // True when anything is stored under key, readable or not, so true does not promise that
        // LoadAsync will succeed.
        UniTask<bool> ExistsAsync(string key, CancellationToken ct);

        // Removes whatever is stored under key. Does nothing when nothing is stored.
        UniTask DeleteAsync(string key, CancellationToken ct);

        // True when every call above finishes on the thread that started it. Only then is it safe
        // to block on one of these tasks for its result; blocking when this is false risks deadlock.
        bool CompletesOnCallingThread { get; }
    }
}
