using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Config
{
    /// <summary>
    /// Where the raw config document comes from.
    /// </summary>
    /// <remarks>
    /// See docs/design-decisions.md, "4. Fetching split from parsing in the config".
    /// </remarks>
    public interface IGameConfigSource
    {
        /// <summary>
        /// Null when the source reached its document slot and found nothing in it. A source that
        /// cannot reach the document at all throws instead.
        /// </summary>
        UniTask<string> ReadAsync(CancellationToken ct);
    }
}
