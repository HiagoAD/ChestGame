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

        // Durable once this completes: a read afterwards returns what was written, even across a
        // process restart.
        UniTask SaveAsync<T>(string key, T state, CancellationToken ct) where T : class;

        UniTask<bool> ExistsAsync(string key, CancellationToken ct);

        UniTask DeleteAsync(string key, CancellationToken ct);

        // True when every call above finishes on the thread that started it. Only then is it safe
        // to block on one of these tasks for its result; blocking when this is false risks deadlock.
        bool CompletesOnCallingThread { get; }
    }
}
