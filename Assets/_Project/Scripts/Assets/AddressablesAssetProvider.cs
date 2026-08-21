using System;
using System.Threading;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Assets
{
    // The production provider, and the only class in the project that calls Addressables.
    //
    // Its second job is translation. Addressables reports both of its failure modes as its own
    // exception types, which would leak the loading technology into every catch site and would let
    // a test asserting "this throws" be satisfied by something unrelated. They are turned into the
    // project's own hierarchy here: a key that is not in the catalog is a MissingAssetException,
    // anything else that went wrong while loading is an AssetLoadException.
    public class AddressablesAssetProvider : IAssetProvider
    {
        // A label names a set rather than one asset, so there is no type to report the way the two
        // load routes report theirs.
        private const string CONTENT_KIND = "Content";

        // What was loaded through the reference route. Its own class because the key it uses is a
        // rule rather than a detail; see AssetHandleRegistry.
        private readonly AssetHandleRegistry _handles = new();

        public async UniTask<TAsset> LoadAsync<TAsset>(string key, CancellationToken ct) where TAsset : Object
        {
            try
            {
                AsyncOperationHandle<TAsset> handle = Addressables.LoadAssetAsync<TAsset>(key);

                // ToUniTask rather than awaiting the handle directly: UniTask version-gates some of
                // its awaiter extensions, so the explicit call is the form that cannot bind to the
                // wrong overload. See docs/context/self-contained-minigames.md section 6.
                return await handle.ToUniTask(cancellationToken: ct);
            }
            catch (InvalidKeyException exception)
            {
                throw new MissingAssetException(key, typeof(TAsset).Name, exception);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is not a failure to load; it is the caller changing its mind, and
                // the whole content load is already shaped to unwind on it.
                throw;
            }
            catch (Exception exception)
            {
                throw new AssetLoadException(key, exception);
            }
        }

        public async UniTask<TAsset> LoadAsync<TAsset>(AssetReference reference, CancellationToken ct)
            where TAsset : Object
        {
            // The reference is the key as far as reporting goes: it is the GUID the inspector
            // wrote, and it is what a reader has to look up to find the offending slot.
            string key = KeyOf(reference);

            if (reference == null || !reference.RuntimeKeyIsValid())
            {
                // Caught here rather than left to Addressables, which would log an error on its way
                // to throwing over a key that was never authored in the first place.
                throw new MissingAssetException(key, typeof(TAsset).Name);
            }

            try
            {
                // The reference is handed over as the key rather than loaded through its own
                // LoadAssetAsync, which stores the handle on the reference itself. That field lives
                // on a shared definition asset, so a second load would log an error and lose the
                // first handle. The bookkeeping belongs to the provider, which is also what lets
                // Release take a reference and hand the caller no Addressables type.
                AsyncOperationHandle<TAsset> handle = Addressables.LoadAssetAsync<TAsset>(reference);
                TAsset asset = await handle.ToUniTask(cancellationToken: ct);

                _handles.Remember(reference, handle);

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
        }

        public void Release(AssetReference reference)
        {
            foreach (AsyncOperationHandle handle in _handles.Take(reference))
            {
                if (handle.IsValid()) Addressables.Release(handle);
            }
        }

        public async UniTask<long> GetDownloadSizeAsync(string label, CancellationToken ct)
        {
            AsyncOperationHandle<long> handle = default;
            try
            {
                handle = Addressables.GetDownloadSizeAsync(label);

                // Content that is already cached, or that shipped local, has nothing left to come
                // down and answers zero. Addressables computes that itself, which is why the empty
                // case needs no special path here.
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
                // The size query is an operation like any other, so it holds a handle until it is
                // let go of. Released here rather than through autoReleaseHandle, which hands back
                // a handle that is already invalid and would make the await path ambiguous.
                if (handle.IsValid()) Addressables.Release(handle);
            }
        }

        public async UniTask DownloadAsync(string label, IProgress<float> progress, CancellationToken ct)
        {
            AsyncOperationHandle handle = default;
            try
            {
                // Downloads into the cache without loading anything: the bundles are fetched, and
                // whatever actually wants an asset out of them still goes through LoadAsync later.
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
