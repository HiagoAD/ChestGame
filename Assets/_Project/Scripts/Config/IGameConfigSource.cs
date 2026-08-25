using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Config
{
    // Where the raw config document comes from. Splitting fetching from parsing is what lets the
    // local JSON loader be swapped for a real remote config without touching the validation rules.
    public interface IGameConfigSource
    {
        // Null when the source reached its document slot and found nothing in it. A source that
        // cannot reach the document at all throws instead, which is a different failure.
        UniTask<string> ReadAsync(CancellationToken ct);
    }
}
