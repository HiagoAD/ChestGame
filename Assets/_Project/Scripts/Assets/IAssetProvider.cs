using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Assets
{
    // The seam over how assets are fetched. Every source asks this rather than a loader, which is
    // what keeps the loading technology inside one assembly: nothing outside
    // Company.ChestGame.Assets calls Addressables.
    //
    // Two routes in, because content is named two different ways. A source that owns a key uses the
    // string route. A definition asset that was authored pointing at its own content carries an
    // AssetReference, which is a GUID the inspector filled in rather than a hard object reference,
    // and that indirection is the only reason a minigame's bundle is not dragged in by the mere act
    // of loading its descriptor.
    //
    // Naming AssetReference here is what forces an assembly authoring one to reference the package
    // for the serializable type, even when it only ever calls the key route.
    public interface IAssetProvider
    {
        // Throws MissingAssetException when the key is not in the shipped catalog, and
        // AssetLoadException when the key resolved but the load itself failed.
        UniTask<TAsset> LoadAsync<TAsset>(string key, CancellationToken ct) where TAsset : Object;

        // Same two failure modes, reached through an authored reference instead of a key. An
        // unwired or unresolvable reference is the missing case.
        UniTask<TAsset> LoadAsync<TAsset>(AssetReference reference, CancellationToken ct) where TAsset : Object;

        // Drops what this provider loaded for that reference. Safe on a reference that was never
        // loaded, and on a null one, because the teardown paths that call it are documented as safe
        // to call unconditionally and would otherwise all need the same guard.
        void Release(AssetReference reference);

        // How many bytes still have to come down the wire before everything under that label can be
        // loaded. The unit the delivery story works in is the label rather than the key, because a
        // label is what names a whole minigame's content at once, which is the thing a player is
        // made to wait for.
        //
        // Zero is the ordinary answer, not an error: it is what content that is already cached, or
        // that shipped inside the player, reports. Same two failure modes as a load.
        UniTask<long> GetDownloadSizeAsync(string label, CancellationToken ct);

        // Fetches everything under that label into the cache without loading any of it, reporting
        // 0..1 as it goes. Nothing left to fetch completes immediately rather than failing, so a
        // caller does not have to ask first — it asks first only because it wants the size.
        UniTask DownloadAsync(string label, IProgress<float> progress, CancellationToken ct);
    }
}
