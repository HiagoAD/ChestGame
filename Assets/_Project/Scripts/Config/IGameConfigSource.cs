using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Config
{
    // Where the raw config document comes from. Splitting fetching from parsing is what lets the
    // local JSON loader be swapped for a real remote config later without touching the parsing or
    // validation rules, and it makes the failure surface (missing document, malformed payload)
    // reachable from a unit test.
    //
    // Asynchronous because a real source downloads. The local Resources-backed one hands back an
    // already-completed task, so nothing waits today, but every caller is already shaped for the
    // day one of them does.
    public interface IGameConfigSource
    {
        // Returns the raw config document, or null when no document could be found.
        UniTask<string> ReadAsync(CancellationToken ct);
    }
}
