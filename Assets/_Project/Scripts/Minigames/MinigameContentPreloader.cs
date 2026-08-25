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
    // Fetches, before the player can ask for any of it, the content of every minigame whose
    // descriptor says it wants to arrive that way. A plain class with no scene and no scope, so the
    // bootstrapper is left holding one call and this can run against a fake provider in edit mode.
    public class MinigameContentPreloader
    {
        private readonly IMinigameCatalog _catalog;
        private readonly IAssetProvider _assets;

        public MinigameContentPreloader(IMinigameCatalog catalog, IAssetProvider assets)
        {
            _catalog = catalog;
            _assets = assets;
        }

        // How long any single label may go without answering before boot gives up on it. Per label
        // rather than across the whole preload, so the budget measures a stall rather than the
        // amount of content. See docs/content-delivery.md.
        protected virtual TimeSpan LabelDownloadTimeout => TimeSpan.FromSeconds(90);

        // Progress is aggregate rather than per label, which is why the sizes are gathered first:
        // the share each label is worth cannot be known until the whole total is.
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

            // Everything is already cached or shipped local: nothing to wait for, nothing to say.
            if (total <= 0) return;

            long fetched = 0;
            for (int i = 0; i < labels.Count; i++)
            {
                string label = labels[i];
                IProgress<float> share = ShareOf(progress, fetched, sizes[i], total);

                // AsAsyncUnitUniTask because Bounded is generic and this route returns nothing.
                await Bounded(
                    token => _assets.DownloadAsync(label, share, token).AsAsyncUnitUniTask(), label, ct);

                fetched += sizes[i];

                // Reported again on completion rather than trusting the inner reporter to have
                // finished at exactly its own 1.
                progress?.Report((float)fetched / total);
            }
        }

        private List<string> LabelsToPreload()
        {
            List<string> labels = new();

            // The type-keyed lookup: an entry whose id was never authored is missing from the
            // id-keyed one, and its content still has to arrive.
            foreach (MinigameBaseSO minigame in _catalog.Minigames.Values)
            {
                if (minigame.LoadPolicy != MinigameLoadPolicy.Preload) continue;

                // The blank-label rule belongs to the descriptor, which owns the field.
                if (!minigame.TryGetContentLabel(out string label)) continue;

                labels.Add(label);
            }

            return labels;
        }

        // A stalled fetch is not a failed one: nothing throws, nothing returns, and the boot screen
        // sits on "Preparing content..." indefinitely. The linked source ends the wait when the app
        // is quitting.
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
            // Boot being cancelled is the application quitting, so it travels out untouched. Both
            // at once counts as the caller's, which is the safe way round.
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new ContentDownloadTimeoutException(label, budget);
            }
        }

        private static IProgress<float> ShareOf(IProgress<float> outer, long already, long size, long total) =>
            outer == null || size <= 0 ? null : new AggregateProgress(outer, already, size, total);

        // Maps one label's own 0..1 onto the slice of the whole download that label is worth.
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
