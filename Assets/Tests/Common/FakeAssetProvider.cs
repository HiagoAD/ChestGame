using System;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Assets;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.Common
{
    /// <summary>
    /// Hands back what a test put in, records every key, reference, release and download asked
    /// for, and can be told to fail the way a real fetch fails. Releases and downloads are
    /// recorded because neither leaves a trace on the caller.
    /// </summary>
    /// <remarks>
    /// See docs/testing.md, "What lives where".
    /// </remarks>
    public class FakeAssetProvider : IAssetProvider
    {
        private readonly Dictionary<string, Object> _assetsByKey = new();
        private readonly Dictionary<AssetReference, Object> _assetsByReference = new();
        private readonly Dictionary<AssetReference, Exception> _failuresByReference = new();
        private readonly Dictionary<string, long> _downloadSizes = new();
        private readonly List<UniTaskCompletionSource> _uncancellableStalls = new();

        public List<string> RequestedKeys { get; } = new();
        public List<AssetReference> RequestedReferences { get; } = new();
        public List<AssetReference> ReleasedReferences { get; } = new();

        public List<string> SizedLabels { get; } = new();
        public List<string> DownloadedLabels { get; } = new();

        /// <summary>
        /// Both delivery routes in one ordered log.
        /// </summary>
        /// <remarks>
        /// See docs/content-delivery.md, "When content arrives".
        /// </remarks>
        public List<string> ContentCalls { get; } = new();

        /// <summary>
        /// When set, every load, size query and download fails with this exception, delivered
        /// through the returned task rather than thrown from the call.
        /// </summary>
        public Exception FailWith { get; set; }

        /// <summary>
        /// Fails only <see cref="DownloadAsync"/>, so a test can let the size query succeed and
        /// reach the code that runs between the two.
        /// </summary>
        public Exception FailDownloadWith { get; set; }

        /// <summary>
        /// A download that neither finishes nor fails. It still ends when the token it was handed
        /// is cancelled, exactly as the real provider does, unless
        /// <see cref="StalledDownloadsIgnoreCancellation"/> is set.
        /// </summary>
        /// <remarks>
        /// See docs/content-delivery.md, "Timeouts".
        /// </remarks>
        public bool StallDownloads { get; set; }

        /// <summary>
        /// Under <see cref="StallDownloads"/>, makes a stalled download ignore the token it was
        /// handed and end only when <see cref="CompleteStalledDownloads"/> is called: a fetch that
        /// finishes after its caller has already given up. Off by default, and read when the
        /// download is asked for.
        /// </summary>
        public bool StalledDownloadsIgnoreCancellation { get; set; }

        /// <summary>
        /// Under <see cref="StalledDownloadsIgnoreCancellation"/>, also registers a callback on the
        /// stalled download's token that throws, so cancelling that token makes the cancelling
        /// call throw an <see cref="AggregateException"/> while the download itself stays pending.
        /// Off by default, and read when the download is asked for.
        /// </summary>
        public bool StalledDownloadsThrowOnCancellation { get; set; }

        /// <summary>
        /// What a finishing download reports, in order. Just the final 1 unless a test asks for
        /// the steps in between.
        /// </summary>
        /// <remarks>
        /// See docs/content-delivery.md, "Progress reporting".
        /// </remarks>
        public float[] DownloadProgressSteps { get; set; } = new[] { 1f };

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

        /// <summary>
        /// Registers the size <see cref="GetDownloadSizeAsync"/> reports for
        /// <paramref name="label"/>. Zero unless a test says otherwise: "nothing left to
        /// download" is the ordinary answer.
        /// </summary>
        public FakeAssetProvider WithDownloadSize(string label, long size)
        {
            _downloadSizes[label] = size;
            return this;
        }

        /// <summary>
        /// Fails only the load of <paramref name="reference"/>, the only way to reach the state
        /// where one load already succeeded and the next did not.
        /// </summary>
        public FakeAssetProvider FailingOn(AssetReference reference, Exception exception)
        {
            _failuresByReference[reference] = exception;
            return this;
        }

        /// <remarks>
        /// Returns null rather than throwing when <paramref name="key"/> was never registered
        /// through <c>With</c>, so the caller's own guards for an empty slot stay reachable in a
        /// test.
        /// </remarks>
        public UniTask<TAsset> LoadAsync<TAsset>(string key, CancellationToken ct) where TAsset : Object
        {
            RequestedKeys.Add(key);
            LastToken = ct;

            if (FailWith != null)
            {
                return UniTask.FromException<TAsset>(FailWith);
            }

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

        /// <remarks>
        /// Reports each value of <see cref="DownloadProgressSteps"/> in order (by default just 1), so a
        /// caller aggregating several labels is not left looking correct while never having been driven.
        /// </remarks>
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

                if (StalledDownloadsIgnoreCancellation)
                {
                    _uncancellableStalls.Add(stalled);

                    if (StalledDownloadsThrowOnCancellation)
                    {
                        ct.Register(() => throw new InvalidOperationException(
                            $"{nameof(FakeAssetProvider)}: a cancellation callback that throws"));
                    }
                }
                else
                {
                    ct.Register(() => stalled.TrySetCanceled(ct));
                }

                return stalled.Task;
            }

            if (progress != null)
            {
                foreach (float step in DownloadProgressSteps)
                {
                    progress.Report(step);
                }
            }
            return UniTask.CompletedTask;
        }

        /// <summary>
        /// Finishes, successfully, every download stalled under
        /// <see cref="StalledDownloadsIgnoreCancellation"/>, running its awaiting continuations
        /// before this returns. Does nothing when none is stalled.
        /// </summary>
        public void CompleteStalledDownloads()
        {
            UniTaskCompletionSource[] stalls = _uncancellableStalls.ToArray();
            _uncancellableStalls.Clear();

            foreach (UniTaskCompletionSource stalled in stalls)
            {
                stalled.TrySetResult();
            }
        }
    }
}
