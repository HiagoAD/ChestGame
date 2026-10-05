using System;
using System.Threading;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Assets
{
    /// <summary>
    /// The production provider, and the only class in the project that calls Addressables.
    /// </summary>
    /// <remarks>
    /// See docs/asset-loading.md, "AddressablesAssetProvider".
    /// </remarks>
    public class AddressablesAssetProvider : IAssetProvider
    {
        /// <summary>
        /// A label names a set rather than one asset, so there is no type to report.
        /// </summary>
        private const string CONTENT_KIND = "Content";

        private readonly AssetHandleRegistry _handles = new();

        /// <remarks>
        /// See docs/asset-loading.md, "Awaiting Addressables handles".
        /// See docs/asset-loading.md, "Translating failures".
        /// See docs/asset-loading.md, "Not leaking a load nobody received".
        /// </remarks>
        public async UniTask<TAsset> LoadAsync<TAsset>(string key, CancellationToken ct) where TAsset : Object
        {
            AsyncOperationHandle<TAsset> handle = default;
            bool delivered = false;
            try
            {
                handle = Addressables.LoadAssetAsync<TAsset>(key);

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
                if (!delivered && handle.IsValid()) Addressables.Release(handle);
            }
        }

        /// <remarks>
        /// See docs/asset-loading.md, "Translating failures".
        /// See docs/asset-loading.md, "Not leaking a load nobody received".
        /// </remarks>
        public async UniTask<TAsset> LoadAsync<TAsset>(AssetReference reference, CancellationToken ct)
            where TAsset : Object
        {
            string key = KeyOf(reference);

            if (reference == null || !reference.RuntimeKeyIsValid())
            {
                throw new MissingAssetException(key, typeof(TAsset).Name);
            }

            bool remembered = false;
            bool delivered = false;
            try
            {
                AsyncOperationHandle<TAsset> handle = Addressables.LoadAssetAsync<TAsset>(reference);

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
                if (!delivered && remembered) ReleaseOne(reference);
            }
        }

        /// <summary>
        /// Releases one load of <paramref name="reference"/>.
        /// </summary>
        /// <remarks>
        /// See docs/asset-loading.md, "One release per load".
        /// </remarks>
        public void Release(AssetReference reference) => ReleaseOne(reference);

        private void ReleaseOne(AssetReference reference)
        {
            if (!_handles.TryTake(reference, out AsyncOperationHandle handle)) return;

            if (handle.IsValid()) Addressables.Release(handle);
        }

        /// <remarks>
        /// See docs/asset-loading.md, "Not leaking a load nobody received".
        /// </remarks>
        public async UniTask<long> GetDownloadSizeAsync(string label, CancellationToken ct)
        {
            AsyncOperationHandle<long> handle = default;
            try
            {
                handle = Addressables.GetDownloadSizeAsync(label);

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
                if (handle.IsValid()) Addressables.Release(handle);
            }
        }

        public async UniTask DownloadAsync(string label, IProgress<float> progress, CancellationToken ct)
        {
            AsyncOperationHandle handle = default;
            try
            {
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
