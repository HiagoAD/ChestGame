using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Config
{
    // Where the raw config document comes from. Splitting fetching from parsing is what lets the
    // local JSON loader be swapped for a real remote config later without touching the parsing or
    // validation rules, and it makes the failure surface (missing document, malformed payload)
    // reachable from a unit test.
    //
    // Asynchronous because a real source downloads, and the shipped one now genuinely can wait:
    // it goes through the asset provider, which is backed by Addressables.
    public interface IGameConfigSource
    {
        // Returns the raw config document, or null when the source reached its document slot and
        // found nothing in it. A source that cannot reach the document at all throws instead, as
        // MissingAssetException or AssetLoadException, because that is a different failure from an
        // empty one and the caller can do different things about it.
        UniTask<string> ReadAsync(CancellationToken ct);
    }
}
