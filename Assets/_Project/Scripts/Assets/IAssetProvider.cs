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
    //
    // The two routes are not symmetric about lifetime, and a caller has to know it, because nothing
    // in the compiler or the test suite will tell it.
    //
    // Nothing loaded by key is ever released. There is only Release(AssetReference), so an asset
    // fetched through the string route is resident for the rest of the session. That is a decision
    // rather than an omission: the key route exists for the four things the boot sequence names
    // itself — the config document, the minigame list, the popup list and the popup parent prefab
    // — every one of which is read once and then wanted for as long as the game is running.
    // Loading one of them twice would not pile handles up either, because Addressables hands back
    // the operation it already has; what cannot be undone is the asset staying loaded.
    //
    // So content that is transient — wanted for one screen, one minigame, one popup — has to be
    // reached through an AssetReference, which is the only route that can be let go of again.
    // Loading transient content by key leaks it, and this seam cannot report that and no test will
    // catch it. If a case ever genuinely needs the key route with a lifetime, the answer is to add
    // Release(string) beside it and track key loads the way the reference route is tracked, not to
    // decide the asset is small enough not to matter.
    public interface IAssetProvider
    {
        // Throws MissingAssetException when the key is not in the shipped catalog, and
        // AssetLoadException when the key resolved but the load itself failed.
        //
        // Resident for the session once it arrives: there is no Release for a key. See the note on
        // the interface above before reaching for this route with anything transient. A load that
        // is cancelled or that fails lets go of its own ref-count, so only assets actually handed
        // to a caller stay loaded.
        UniTask<TAsset> LoadAsync<TAsset>(string key, CancellationToken ct) where TAsset : Object;

        // Same two failure modes, reached through an authored reference instead of a key. An
        // unwired or unresolvable reference is the missing case.
        //
        // Unlike the key route this one can be undone: every load that hands an asset back leaves
        // exactly one thing for Release to drop. A load that is cancelled or that fails leaves
        // nothing, having already dropped what it took, so a caller only ever releases what it
        // actually received.
        UniTask<TAsset> LoadAsync<TAsset>(AssetReference reference, CancellationToken ct) where TAsset : Object;

        // Drops one of the loads this provider is holding for that reference. Safe on a reference
        // that was never loaded, and on a null one, because the teardown paths that call it are
        // documented as safe to call unconditionally and would otherwise all need the same guard.
        //
        // One release per load, matching what Addressables ref-counts: two live callers that each
        // loaded the same asset each release once, and the first of them to finish does not pull
        // the asset out from under the second. A caller that loads twice and releases once keeps
        // the asset for the session.
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
