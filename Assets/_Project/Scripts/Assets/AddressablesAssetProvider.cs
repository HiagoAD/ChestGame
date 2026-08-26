using System;
using System.Threading;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Assets
{
    // The production provider, and the only class in the project that calls Addressables. Its
    // second job is translating the package's exception types into the project's own hierarchy.
    // See docs/asset-loading.md.
    public class AddressablesAssetProvider : IAssetProvider
    {
        // A label names a set rather than one asset, so there is no type to report.
        private const string CONTENT_KIND = "Content";

        private readonly AssetHandleRegistry _handles = new();

        public async UniTask<TAsset> LoadAsync<TAsset>(string key, CancellationToken ct) where TAsset : Object
        {
            AsyncOperationHandle<TAsset> handle = default;
            bool delivered = false;
            try
            {
                handle = Addressables.LoadAssetAsync<TAsset>(key);

                // ToUniTask rather than awaiting the handle: UniTask version-gates some awaiter
                // extensions, so the explicit call cannot bind to the wrong overload.
                TAsset asset = await handle.ToUniTask(cancellationToken: ct);

                delivered = true;
                return asset;
            }
            catch (InvalidKeyException exception)
            {
                throw new MissingAssetException(key, typeof(TAsset).Name, exception);
            }
            // Cancellation is the caller changing its mind, not a failure to load.
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new AssetLoadException(key, exception);
            }
            finally
            {
                // LoadAssetAsync takes the ref-count before anything is awaited, and nothing
                // tracks keys, so a load nobody receives has to let go of it here or it is
                // resident for good.
                if (!delivered && handle.IsValid()) Addressables.Release(handle);
            }
        }

        public async UniTask<TAsset> LoadAsync<TAsset>(AssetReference reference, CancellationToken ct)
            where TAsset : Object
        {
            // The GUID the inspector wrote is what a reader looks up to find the offending slot.
            string key = KeyOf(reference);

            if (reference == null || !reference.RuntimeKeyIsValid())
            {
                // Caught here rather than left to Addressables, which would log an error on its
                // way to throwing over a key that was never authored.
                throw new MissingAssetException(key, typeof(TAsset).Name);
            }

            bool remembered = false;
            bool delivered = false;
            try
            {
                // Handed over as a key rather than loaded through AssetReference.LoadAssetAsync,
                // which stores the handle on the reference itself. That field lives on a shared
                // definition asset, so a second load would lose the first handle.
                AsyncOperationHandle<TAsset> handle = Addressables.LoadAssetAsync<TAsset>(reference);

                // Remembered before the await: a token that fires while the bytes are still
                // coming throws straight past everything below, and an unrecorded handle is one
                // nothing in the session can ever release.
                _handles.Remember(reference, handle);
                remembered = true;

                TAsset asset = await handle.ToUniTask(cancellationToken: ct);

                delivered = true;
                return asset;
            }
            catch (InvalidKeyException exception)
            {
                throw new MissingAssetException(key, typeof(TAsset).Name, exception);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new AssetLoadException(key, exception);
            }
            finally
            {
                // A load that did not deliver drops exactly what it took, which is the newest
                // handle for this key. Conditioned on having recorded something rather than on
                // having failed: releasing without a ref-count of its own would take the handle
                // out from under whoever loaded the same asset first.
                if (!delivered && remembered) ReleaseOne(reference);
            }
        }

        // One release per load, because that is what Addressables counts. Two containers running
        // the same minigame is a supported state, so dropping every handle held for the asset
        // would pull it out from under a live caller.
        public void Release(AssetReference reference) => ReleaseOne(reference);

        private void ReleaseOne(AssetReference reference)
        {
            if (!_handles.TryTake(reference, out AsyncOperationHandle handle)) return;

            if (handle.IsValid()) Addressables.Release(handle);
        }

        public async UniTask<long> GetDownloadSizeAsync(string label, CancellationToken ct)
        {
            AsyncOperationHandle<long> handle = default;
            try
            {
                handle = Addressables.GetDownloadSizeAsync(label);

                // Cached or local content answers zero, computed by Addressables itself, so the
                // empty case needs no special path.
                return await handle.ToUniTask(cancellationToken: ct);
            }
            catch (InvalidKeyException exception)
            {
                throw new MissingAssetException(label, CONTENT_KIND, exception);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new AssetLoadException(label, exception);
            }
            finally
            {
                // Released here rather than through autoReleaseHandle, which hands back a handle
                // that is already invalid and would make the await path ambiguous.
                if (handle.IsValid()) Addressables.Release(handle);
            }
        }

        public async UniTask DownloadAsync(string label, IProgress<float> progress, CancellationToken ct)
        {
            AsyncOperationHandle handle = default;
            try
            {
                // Into the cache without loading anything: whatever wants an asset out of these
                // bundles still goes through LoadAsync later.
                handle = Addressables.DownloadDependenciesAsync(label);

                await handle.ToUniTask(progress: progress, cancellationToken: ct);
            }
            catch (InvalidKeyException exception)
            {
                throw new MissingAssetException(label, CONTENT_KIND, exception);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new AssetLoadException(label, exception);
            }
            finally
            {
                if (handle.IsValid()) Addressables.Release(handle);
            }
        }

        private static string KeyOf(AssetReference reference) => reference?.RuntimeKey?.ToString() ?? "<no reference>";
    }
}
