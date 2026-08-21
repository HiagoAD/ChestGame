using System;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Assets;
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
                sizes[i] = await _assets.GetDownloadSizeAsync(labels[i], ct);
                total += sizes[i];
            }

            // Everything is already cached or shipped local. Nothing to wait for, and nothing worth
            // telling the player about either.
            if (total <= 0) return;

            long fetched = 0;
            for (int i = 0; i < labels.Count; i++)
            {
                await _assets.DownloadAsync(labels[i], ShareOf(progress, fetched, sizes[i], total), ct);

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

                if (string.IsNullOrWhiteSpace(minigame.ContentLabel))
                {
                    // Warned rather than thrown, following the same policy as a blank id in
                    // CatalogBuilder: one unauthored slot should not stop the game from booting,
                    // and the minigame is still startable — its content simply arrives late.
                    Debug.LogWarning(
                        $"Minigame '{minigame.name}' is set to preload but names no content label, skipping it");
                    continue;
                }

                labels.Add(minigame.ContentLabel);
            }

            return labels;
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
