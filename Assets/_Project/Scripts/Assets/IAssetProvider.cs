using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Assets
{
    // The seam over how assets are fetched, so that nothing outside Company.ChestGame.Assets calls
    // Addressables.
    //
    // The two load routes are NOT symmetric about lifetime and nothing in the compiler or the test
    // suite will tell you: anything loaded by key is resident for the session, and only an
    // AssetReference can be released. Loading transient content by key leaks it silently.
    // See docs/asset-loading.md before adding a call site.
    public interface IAssetProvider
    {
        // Throws MissingAssetException when the key is not in the shipped catalog, and
        // AssetLoadException when the key resolved but the load itself failed. Resident for the
        // session once it arrives: there is no Release for a key.
        UniTask<TAsset> LoadAsync<TAsset>(string key, CancellationToken ct) where TAsset : Object;

        // Same two failure modes through an authored reference; an unwired one is the missing
        // case. Every load that hands an asset back leaves exactly one thing for Release to drop,
        // and a cancelled or failed load leaves nothing.
        UniTask<TAsset> LoadAsync<TAsset>(AssetReference reference, CancellationToken ct) where TAsset : Object;

        // One release per load, matching what Addressables ref-counts. Safe on a reference that
        // was never loaded and on a null one, so teardown paths can call it unconditionally.
        void Release(AssetReference reference);

        // Bytes still to come down the wire for everything under that label. Zero is the ordinary
        // answer rather than an error: cached or local content reports it. Same failure modes as a
        // load.
        UniTask<long> GetDownloadSizeAsync(string label, CancellationToken ct);

        // Fetches a label into the cache without loading any of it, reporting 0..1 as it goes.
        // Nothing left to fetch completes immediately rather than failing.
        UniTask DownloadAsync(string label, IProgress<float> progress, CancellationToken ct);
    }
}
