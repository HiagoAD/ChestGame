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
    // descriptor says it wants to arrive that way.
    //
    // The walk lives here rather than in the bootstrapper for the usual reason: a plain class with
    // no scene and no scope in it can be run against a fake provider in edit mode, and what is left
    // in the bootstrapper is one call. It reads nothing but the two content fields on the
    // descriptor, so a minigame decides its own delivery on its own asset.
    public class MinigameContentPreloader
    {
        private readonly IMinigameCatalog _catalog;
        private readonly IAssetProvider _assets;

        public MinigameContentPreloader(IMinigameCatalog catalog, IAssetProvider assets)
        {
            _catalog = catalog;
            _assets = assets;
        }

        // How long any single label is allowed to go without answering before boot gives up on it.
        //
        // Per label rather than across the whole preload, and the difference matters: a wall-clock
        // budget for the entire walk would make boot fail for having *more* content rather than for
        // being stuck, so every minigame added would bring the game closer to a spurious timeout.
        // Bounding each label instead means the budget measures the thing that is actually wrong —
        // one fetch that stopped answering — and a legitimately large preload is never killed for
        // its size. The worst case grows with the number of labels, which is the honest trade: it
        // is bounded, and every step of it is reported to the player.
        //
        // The same ninety seconds MinigameContainer allows an on-demand fetch, and for the same
        // reason: Addressables already gives up after fifteen seconds without a byte and retries
        // twice, so anything the package can bound reaches the player with a better message than
        // "it timed out" long before this fires. This is the backstop for the stalls it cannot.
        protected virtual TimeSpan LabelDownloadTimeout => TimeSpan.FromSeconds(90);

        // Progress is aggregate, not per label: a player watching a bar does not care that the work
        // is split by minigame, and a bar that restarts at zero for every label reads as a bug. The
        // sizes are gathered first for exactly that reason — the share each label is worth cannot
        // be known until the whole total is.
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

            // Everything is already cached or shipped local. Nothing to wait for, and nothing worth
            // telling the player about either.
            if (total <= 0) return;

            long fetched = 0;
            for (int i = 0; i < labels.Count; i++)
            {
                string label = labels[i];
                IProgress<float> share = ShareOf(progress, fetched, sizes[i], total);

                // AsAsyncUnitUniTask because Bounded is generic: the deadline and the two-token
                // distinction are identical for both routes, and one of them has nothing to return.
                await Bounded(
                    token => _assets.DownloadAsync(label, share, token).AsAsyncUnitUniTask(), label, ct);

                fetched += sizes[i];

                // Reported again on completion rather than trusting the inner reporter to have
                // finished at exactly its own 1, so the aggregate is correct at every boundary.
                progress?.Report((float)fetched / total);
            }
        }

        private List<string> LabelsToPreload()
        {
            List<string> labels = new();

            // The type-keyed lookup rather than the id-keyed one: an entry whose id was never
            // authored is missing from the second, and its content still has to arrive.
            foreach (MinigameBaseSO minigame in _catalog.Minigames.Values)
            {
                if (minigame.LoadPolicy != MinigameLoadPolicy.Preload) continue;

                // The rule itself belongs to the descriptor, which owns the field; both delivery
                // paths ask it the same question and get the same answer.
                if (!minigame.TryGetContentLabel(out string label)) continue;

                labels.Add(label);
            }

            return labels;
        }

        // A stalled fetch is not a failed one: nothing throws, nothing returns, and the boot screen
        // sits on "Preparing content..." for as long as the player is willing to watch it. The
        // linked source ends the wait immediately when the app is quitting, and only the deadline
        // firing on its own becomes something the player is told about — the same distinction
        // MinigameContainer keeps on the on-demand path, and for the same reason.
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
            // The caller's token is the only one that can be asked after the fact which end fired.
            // Boot being cancelled is the application quitting and there is nobody left to tell, so
            // it travels out untouched; both at once counts as the caller's, which is the safe way
            // round.
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
