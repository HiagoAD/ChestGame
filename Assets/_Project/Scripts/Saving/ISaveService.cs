using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Saving
{
    // The seam the game saves and loads through. See docs/saving.md.
    public interface ISaveService
    {
        // Returns a new T() when nothing is stored: a first run has no save to lose. Throws
        // SaveException when something IS stored and cannot be read, rather than also returning a
        // fresh T, which would make a corrupt save indistinguishable from a first run.
        UniTask<T> LoadAsync<T>(string key, CancellationToken ct) where T : class, new();

        UniTask SaveAsync<T>(string key, T state, CancellationToken ct) where T : class;

        UniTask<bool> ExistsAsync(string key, CancellationToken ct);

        UniTask DeleteAsync(string key, CancellationToken ct);
    }
}
