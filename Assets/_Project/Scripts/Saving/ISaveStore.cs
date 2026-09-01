using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Saving
{
    // Where bytes land, keyed by string. Knows nothing about envelopes, codecs or protectors.
    public interface ISaveStore
    {
        UniTask WriteAsync(string key, byte[] bytes, CancellationToken ct);

        // Null when nothing is stored under key. Something stored that cannot be read is thrown.
        UniTask<byte[]> ReadAsync(string key, CancellationToken ct);

        UniTask<bool> ExistsAsync(string key, CancellationToken ct);

        UniTask DeleteAsync(string key, CancellationToken ct);
    }
}
