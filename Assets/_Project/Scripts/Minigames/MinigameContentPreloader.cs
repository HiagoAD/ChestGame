using System;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Company.ChestGame.Minigame.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Minigame
{
    /// <summary>
    /// Fetches, before the player can ask for any of it, the content of every minigame whose
    /// descriptor says it wants to arrive that way.
    /// </summary>
    /// <remarks>
    /// See docs/content-delivery.md, "When content arrives".
    /// </remarks>
    public class MinigameContentPreloader
    {
        private readonly IMinigameCatalog _catalog;
        private readonly IAssetProvider _assets;

        public MinigameContentPreloader(IMinigameCatalog catalog, IAssetProvider assets)
        {
            _catalog = catalog;
            _assets = assets;
        }

        /// <summary>
        /// How long a single label's fetch may go unanswered before boot gives up on it.
        /// </summary>
        /// <remarks>
        /// See docs/content-delivery.md, "Timeouts".
        /// </remarks>
        protected virtual TimeSpan LabelDownloadTimeout => TimeSpan.FromSeconds(90);

        /// <summary>
        /// Downloads every label whose minigame is set to preload, reporting one aggregate
        /// progress figure across all of them.
        /// </summary>
        /// <param name="progress">
        /// Reports the aggregate fraction downloaded across every preloaded label; may be null.
        /// </param>
        /// <exception cref="AssetLoadException">A label resolved but the size query failed.</exception>
        /// <exception cref="MissingAssetException">A label is not in the shipped catalog.</exception>
        /// <exception cref="ContentDownloadTimeoutException">A label's fetch stalled past its timeout.</exception>
        /// <remarks>
        /// See docs/content-delivery.md, "When content arrives", "Progress reporting" and "Timeouts".
        /// </remarks>
        public async UniTask PreloadAsync(IProgress<float> progress, CancellationToken ct)
        {
            List<string> labels = LabelsToPreload();
            if (labels.Count == 0) return;

            long[] sizes = new long[labels.Count];
            long total = 0;

            for (int i = 0; i < labels.Count; i++)
            {
                string label = labels[i];
                sizes[i] = await Bounded(token => _assets.GetDownloadSizeAsync(label, token), label, ct);
                total += sizes[i];
            }

            if (total <= 0) return;

            long fetched = 0;
            for (int i = 0; i < labels.Count; i++)
            {
                string label = labels[i];
                IProgress<float> share = ShareOf(progress, fetched, sizes[i], total);

                await Bounded(
                    token => _assets.DownloadAsync(label, share, token).AsAsyncUnitUniTask(), label, ct);

                fetched += sizes[i];

                progress?.Report((float)fetched / total);
            }
        }

        /// <remarks>
        /// See docs/content-delivery.md, "When content arrives".
        /// </remarks>
        private List<string> LabelsToPreload()
        {
            List<string> labels = new();

            foreach (MinigameBaseSO minigame in _catalog.Minigames.Values)
            {
                if (minigame.LoadPolicy != MinigameLoadPolicy.Preload) continue;

                if (!minigame.TryGetContentLabel(out string label)) continue;

                labels.Add(label);
            }

            return labels;
        }

        /// <summary>
        /// Runs <paramref name="operation"/> under a deadline linked to <paramref name="ct"/>.
        /// </summary>
        /// <exception cref="ContentDownloadTimeoutException">
        /// The deadline elapsed before <paramref name="operation"/> answered.
        /// </exception>
        /// <remarks>
        /// See docs/content-delivery.md, "Timeouts" and "Which token fired".
        /// </remarks>
        private async UniTask<T> Bounded<T>(
            Func<CancellationToken, UniTask<T>> operation, string label, CancellationToken ct)
        {
            TimeSpan budget = LabelDownloadTimeout;

            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(budget);

            try
            {
                return await operation(deadline.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new ContentDownloadTimeoutException(label, budget);
            }
        }

        private static IProgress<float> ShareOf(IProgress<float> outer, long already, long size, long total) =>
            outer == null || size <= 0 ? null : new AggregateProgress(outer, already, size, total);

        /// <summary>
        /// Maps one label's own 0..1 onto the slice of the whole download that label is worth.
        /// </summary>
        private sealed class AggregateProgress : IProgress<float>
        {
            private readonly IProgress<float> _outer;
            private readonly long _already;
            private readonly long _size;
            private readonly long _total;

            public AggregateProgress(IProgress<float> outer, long already, long size, long total)
            {
                _outer = outer;
                _already = already;
                _size = size;
                _total = total;
            }

            public void Report(float value) => _outer.Report((_already + value * _size) / _total);
        }
    }
}
