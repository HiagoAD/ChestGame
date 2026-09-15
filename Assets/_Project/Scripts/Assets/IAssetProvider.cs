using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Assets
{
    /// <summary>
    /// The seam over how assets are fetched. Nothing outside <c>Company.ChestGame.Assets</c> calls
    /// Addressables directly.
    /// </summary>
    /// <remarks>
    /// The two load routes are not symmetric about lifetime: anything loaded by key is resident for
    /// the session, and only an asset loaded through an <see cref="AssetReference"/> can be
    /// released. Loading transient content by key leaks it.
    /// See docs/asset-loading.md, "Lifetimes: the asymmetry that will bite you".
    /// </remarks>
    public interface IAssetProvider
    {
        /// <summary>
        /// Loads the asset at <paramref name="key"/>.
        /// </summary>
        /// <exception cref="AssetLoadException">The key resolved but the load itself failed.</exception>
        /// <remarks>
        /// Throws <c>MissingAssetException</c> when the key is not in the shipped catalog.
        /// Resident for the session once it arrives: there is no <c>Release</c> for a key.
        /// </remarks>
        UniTask<TAsset> LoadAsync<TAsset>(string key, CancellationToken ct) where TAsset : Object;

        /// <summary>
        /// Loads the asset named by <paramref name="reference"/>.
        /// </summary>
        /// <exception cref="AssetLoadException">The reference resolved but the load itself failed.</exception>
        /// <remarks>
        /// Throws <c>MissingAssetException</c> when the reference is unwired or unresolvable.
        /// Every load that hands an asset back leaves exactly one thing for <see cref="Release"/>
        /// to drop, and a cancelled or failed load leaves nothing.
        /// </remarks>
        UniTask<TAsset> LoadAsync<TAsset>(AssetReference reference, CancellationToken ct) where TAsset : Object;

        /// <summary>
        /// Releases one load of the asset named by <paramref name="reference"/>.
        /// </summary>
        /// <remarks>
        /// One release per load, matching what Addressables ref-counts. Safe on a reference that
        /// was never loaded and on a null one, so teardown paths can call it unconditionally.
        /// </remarks>
        void Release(AssetReference reference);

        /// <summary>
        /// The bytes still to come down the wire for everything under <paramref name="label"/>.
        /// </summary>
        /// <returns>Zero rather than an error when nothing is left to fetch: cached or local
        /// content reports it.</returns>
        /// <exception cref="AssetLoadException">The label resolved but the query itself failed.</exception>
        /// <remarks>
        /// Throws <c>MissingAssetException</c> when the label is not in the shipped catalog.
        /// </remarks>
        UniTask<long> GetDownloadSizeAsync(string label, CancellationToken ct);

        /// <summary>
        /// Fetches everything under <paramref name="label"/> into the cache without loading any of
        /// it, reporting 0..1 as it goes. Whatever wants an asset out of the label still goes
        /// through <c>LoadAsync</c> afterward.
        /// </summary>
        /// <remarks>
        /// Nothing left to fetch completes immediately rather than failing.
        /// </remarks>
        UniTask DownloadAsync(string label, IProgress<float> progress, CancellationToken ct);
    }
}
