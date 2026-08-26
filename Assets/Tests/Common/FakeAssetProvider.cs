using System;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Assets;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.Common
{
    // Stands in for the whole loading technology, which is what keeps the edit-mode suite off
    // Addressables the way FakeGameClock keeps it off the player loop. It hands back what a test
    // put in, records every key, reference, release and download asked for, and can be told to fail
    // the way a real fetch fails. Releases and downloads are recorded because neither leaves a
    // trace on the caller.
    public class FakeAssetProvider : IAssetProvider
    {
        private readonly Dictionary<string, Object> _assetsByKey = new();
        private readonly Dictionary<AssetReference, Object> _assetsByReference = new();
        private readonly Dictionary<AssetReference, Exception> _failuresByReference = new();
        private readonly Dictionary<string, long> _downloadSizes = new();

        public List<string> RequestedKeys { get; } = new();
        public List<AssetReference> RequestedReferences { get; } = new();
        public List<AssetReference> ReleasedReferences { get; } = new();

        public List<string> SizedLabels { get; } = new();
        public List<string> DownloadedLabels { get; } = new();

        // Both delivery routes in one ordered log, because the order between them is a design
        // decision: every size is asked for before anything is fetched.
        public List<string> ContentCalls { get; } = new();

        // Delivered through the returned task rather than thrown from the call, the way a provider
        // that actually waits would report a failure.
        public Exception FailWith { get; set; }

        // Its own knob, so a test can let the size query succeed and fail only the fetch, which is
        // the only way to reach the code that runs between the two.
        public Exception FailDownloadWith { get; set; }

        // A download that neither finishes nor fails, which is the failure mode a deadline exists
        // for. It still ends when the token it was handed is cancelled, exactly as the real
        // provider does.
        public bool StallDownloads { get; set; }

        public CancellationToken LastToken { get; private set; }

        public FakeAssetProvider With(string key, Object asset)
        {
            _assetsByKey[key] = asset;
            return this;
        }

        public FakeAssetProvider With(AssetReference reference, Object asset)
        {
            _assetsByReference[reference] = asset;
            return this;
        }

        // Zero unless a test says otherwise, because "nothing left to download" is the ordinary
        // answer.
        public FakeAssetProvider WithDownloadSize(string label, long size)
        {
            _downloadSizes[label] = size;
            return this;
        }

        // Failing one reference rather than everything, the only way to reach the state where a
        // load already succeeded and the next one did not.
        public FakeAssetProvider FailingOn(AssetReference reference, Exception exception)
        {
            _failuresByReference[reference] = exception;
            return this;
        }

        public UniTask<TAsset> LoadAsync<TAsset>(string key, CancellationToken ct) where TAsset : Object
        {
            RequestedKeys.Add(key);
            LastToken = ct;

            if (FailWith != null)
            {
                return UniTask.FromException<TAsset>(FailWith);
            }

            // Null rather than a throw, so the guards the sources keep for an empty slot stay
            // reachable.
            _assetsByKey.TryGetValue(key, out Object asset);
            return UniTask.FromResult(asset as TAsset);
        }

        public UniTask<TAsset> LoadAsync<TAsset>(AssetReference reference, CancellationToken ct) where TAsset : Object
        {
            RequestedReferences.Add(reference);
            LastToken = ct;

            if (_failuresByReference.TryGetValue(reference, out Exception failure))
            {
                return UniTask.FromException<TAsset>(failure);
            }

            if (FailWith != null)
            {
                return UniTask.FromException<TAsset>(FailWith);
            }

            _assetsByReference.TryGetValue(reference, out Object asset);
            return UniTask.FromResult(asset as TAsset);
        }

        public void Release(AssetReference reference) => ReleasedReferences.Add(reference);

        public UniTask<long> GetDownloadSizeAsync(string label, CancellationToken ct)
        {
            SizedLabels.Add(label);
            ContentCalls.Add($"size:{label}");
            LastToken = ct;

            if (FailWith != null)
            {
                return UniTask.FromException<long>(FailWith);
            }

            _downloadSizes.TryGetValue(label, out long size);
            return UniTask.FromResult(size);
        }

        public UniTask DownloadAsync(string label, IProgress<float> progress, CancellationToken ct)
        {
            DownloadedLabels.Add(label);
            ContentCalls.Add($"download:{label}");
            LastToken = ct;

            Exception failure = FailDownloadWith ?? FailWith;
            if (failure != null)
            {
                return UniTask.FromException(failure);
            }

            if (StallDownloads)
            {
                UniTaskCompletionSource stalled = new();
                ct.Register(() => stalled.TrySetCanceled(ct));

                return stalled.Task;
            }

            // A download that finishes reports that it finished, or a caller aggregating several
            // labels would look correct while never having been driven.
            progress?.Report(1f);
            return UniTask.CompletedTask;
        }
    }
}
