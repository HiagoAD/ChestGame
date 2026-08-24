using System;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Assets;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.Common
{
    // Stands in for the whole loading technology. This is what keeps the edit-mode suite from ever
    // touching Addressables, the same way FakeGameClock keeps it off the player loop: a source can
    // be asked what key it wants and what it does with the answer, with no catalog, no bundle and
    // no initialization behind it.
    //
    // It hands back what a test put in, records every key and reference asked for so a caller's one
    // job — knowing what it wants — is assertable, and can be told to fail the way a real fetch
    // fails. Releases are recorded too, because "what did teardown let go of" is otherwise
    // invisible: a released handle leaves no trace on the caller. Downloads are recorded the same
    // way and for the same reason: the whole point of the delivery work is what is asked for and in
    // what order, and none of that is observable from the assets that come back.
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
        // decision — every size is asked for before anything is fetched — and two separate lists
        // cannot show whether they interleaved.
        public List<string> ContentCalls { get; } = new();

        // Delivered through the returned task rather than thrown from the call, which is how a
        // provider that actually waits on something would report a failure.
        public Exception FailWith { get; set; }

        // Downloading has its own knob, so a test can let the size query succeed and fail only the
        // fetch — which is the shape of a real delivery failure and the only way to reach the code
        // that runs between the two.
        public Exception FailDownloadWith { get; set; }

        // A download that neither finishes nor fails, which is the failure mode a deadline exists
        // for and the one no other knob here can produce: a stalled request holds its socket open
        // and answers nothing, so a caller with no deadline waits for the rest of the session.
        //
        // It still ends when the token it was handed is cancelled, exactly as the real provider
        // does — without that, a deadline would have nothing to act on and could not be observed
        // from a test at all.
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

        // How much this label still has to fetch. Zero unless a test says otherwise, because
        // "nothing left to download" is the ordinary answer a real provider gives.
        public FakeAssetProvider WithDownloadSize(string label, long size)
        {
            _downloadSizes[label] = size;
            return this;
        }

        // Failing one reference rather than everything, which is the only way to reach the state
        // where a load already succeeded and the next one did not.
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

            // Nothing authored at the key hands back null rather than throwing, so the guards the
            // sources keep for an empty slot stay reachable from a test.
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

            // A download that finishes reports that it finished. Without it a caller aggregating
            // several labels would look correct while never having been driven at all.
            progress?.Report(1f);
            return UniTask.CompletedTask;
        }
    }
}
