using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Threading;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Company.ChestGame.Minigame;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Minigame.Internal;
using Company.ChestGame.Tests.Common;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.EditMode
{
    // What arrives before the player can ask for it, and what deliberately does not. Edit mode
    // against FakeAssetProvider, with no scene and no scope in it.
    public class MinigameContentPreloaderTests
    {
        private const string PRELOAD_LABEL = "minigame.preloaded";
        private const string OTHER_PRELOAD_LABEL = "minigame.also-preloaded";
        private const string ON_DEMAND_LABEL = "minigame.on-demand";

        private readonly List<Object> _created = new();

        private FakeAssetProvider _assets;
        private RecordingProgress _progress;

        [SetUp]
        public void SetUp()
        {
            _assets = new FakeAssetProvider();
            _progress = new RecordingProgress();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }
            _created.Clear();
        }

        [Test]
        public void OnlyMinigamesThatAskedToBePreloaded_AreFetched()
        {
            // An on-demand minigame is one the player may never open, so fetching it up front is
            // the wait this design exists to avoid.
            _assets.WithDownloadSize(PRELOAD_LABEL, 100).WithDownloadSize(ON_DEMAND_LABEL, 900);

            Preload(
                Definition<FirstMinigame>(MinigameLoadPolicy.Preload, PRELOAD_LABEL),
                Definition<SecondMinigame>(MinigameLoadPolicy.OnDemand, ON_DEMAND_LABEL));

            CollectionAssert.AreEqual(new[] { PRELOAD_LABEL }, _assets.SizedLabels,
                "an on-demand minigame is not even measured up front");
            CollectionAssert.AreEqual(new[] { PRELOAD_LABEL }, _assets.DownloadedLabels);
        }

        [Test]
        public void EveryPreloadedLabel_IsMeasuredBeforeAnythingIsFetched()
        {
            // The order is the design: the share of the bar a label is worth cannot be known until
            // every size is in.
            _assets.WithDownloadSize(PRELOAD_LABEL, 30).WithDownloadSize(OTHER_PRELOAD_LABEL, 70);

            Preload(
                Definition<FirstMinigame>(MinigameLoadPolicy.Preload, PRELOAD_LABEL),
                Definition<SecondMinigame>(MinigameLoadPolicy.Preload, OTHER_PRELOAD_LABEL));

            CollectionAssert.AreEquivalent(new[] { PRELOAD_LABEL, OTHER_PRELOAD_LABEL }, _assets.SizedLabels);
            CollectionAssert.AreEquivalent(new[] { PRELOAD_LABEL, OTHER_PRELOAD_LABEL }, _assets.DownloadedLabels);

            Assert.AreEqual(2, _assets.ContentCalls.FindIndex(call => call.StartsWith("download:")),
                "both sizes have to be in before the first byte is fetched, or the shares are guesses");
        }

        [Test]
        public void ProgressIsAggregateAcrossEveryLabel_NotPerLabel()
        {
            // Thirty bytes of a hundred is 0.3 of the whole wait, whichever label they belong to.
            _assets.WithDownloadSize(PRELOAD_LABEL, 30).WithDownloadSize(OTHER_PRELOAD_LABEL, 70);

            Preload(
                Definition<FirstMinigame>(MinigameLoadPolicy.Preload, PRELOAD_LABEL),
                Definition<SecondMinigame>(MinigameLoadPolicy.Preload, OTHER_PRELOAD_LABEL));

            CollectionAssert.IsNotEmpty(_progress.Reported, "nothing reported progress at all");
            Assert.AreEqual(1f, _progress.Reported[_progress.Reported.Count - 1], 0.001f,
                "the whole download has to finish at 1");
            Assert.IsTrue(_progress.Reported.Exists(value => Mathf.Abs(value - 0.3f) < 0.001f),
                "finishing the 30-byte label of 100 total is 0.3 overall, not 1");
            Assert.IsFalse(_progress.Reported.Exists(value => value > 1.001f),
                "aggregate progress cannot exceed 1");

            // The tell for per-label progress is a bar that reaches full and then goes back.
            for (int i = 1; i < _progress.Reported.Count; i++)
            {
                Assert.GreaterOrEqual(_progress.Reported[i], _progress.Reported[i - 1],
                    $"progress went backwards at report {i}, which is what a per-label bar looks like");
            }
        }

        [Test]
        public void AMinigameWithNoContentLabel_IsSkippedWithAWarning()
        {
            // Same policy as a blank id in CatalogBuilder: the minigame still starts, its content
            // just arrives late.
            LogAssert.Expect(LogType.Warning, new Regex("names no content label"));

            _assets.WithDownloadSize(PRELOAD_LABEL, 100);

            Preload(
                Definition<FirstMinigame>(MinigameLoadPolicy.Preload, "   "),
                Definition<SecondMinigame>(MinigameLoadPolicy.Preload, PRELOAD_LABEL));

            CollectionAssert.AreEqual(new[] { PRELOAD_LABEL }, _assets.DownloadedLabels,
                "the blank label must not be asked for, and the good one still has to arrive");
        }

        [Test]
        public void NothingLeftToFetch_DownloadsNothing()
        {
            // Zero is what every run after the first gives. Treating it as work would make boot
            // wait on a download with nothing to do.
            Preload(Definition<FirstMinigame>(MinigameLoadPolicy.Preload, PRELOAD_LABEL));

            CollectionAssert.AreEqual(new[] { PRELOAD_LABEL }, _assets.SizedLabels, "it still has to ask");
            CollectionAssert.IsEmpty(_assets.DownloadedLabels);
            CollectionAssert.IsEmpty(_progress.Reported);
        }

        [Test]
        public void NoMinigameAsksToBePreloaded_AsksNothingAtAll()
        {
            Preload(Definition<FirstMinigame>(MinigameLoadPolicy.OnDemand, ON_DEMAND_LABEL));

            CollectionAssert.IsEmpty(_assets.SizedLabels);
            CollectionAssert.IsEmpty(_assets.DownloadedLabels);
        }

        [Test]
        public void AFailedDownload_SurfacesTheTypedFailure()
        {
            // Typed all the way out, so the shell can tell a delivery problem from a bug. The size
            // query succeeds on purpose: the failure has to survive the code between measuring and
            // fetching.
            _assets.WithDownloadSize(PRELOAD_LABEL, 100);
            _assets.FailDownloadWith = new AssetLoadException(PRELOAD_LABEL, new Exception("no route to host"));

            AssetLoadException failure = Assert.Throws<AssetLoadException>(() =>
                Preload(Definition<FirstMinigame>(MinigameLoadPolicy.Preload, PRELOAD_LABEL)));

            Assert.AreEqual(PRELOAD_LABEL, failure.Key);
        }

        [Test]
        public void TheCancellationToken_ReachesTheProvider()
        {
            // A boot that was abandoned has to stop the download with it. Asserted by behaviour
            // rather than identity, because the provider is handed a token linked to the caller's
            // rather than the caller's own. Cancelled while the fetch is in flight, the only moment
            // the linkage is observable: the linked source is disposed as soon as the fetch
            // returns.
            _assets.WithDownloadSize(PRELOAD_LABEL, 100);
            _assets.StallDownloads = true;

            MinigameContentPreloader preloader = new(
                new MinigameCatalog(new List<MinigameBaseSO>
                {
                    Definition<FirstMinigame>(MinigameLoadPolicy.Preload, PRELOAD_LABEL)
                }),
                _assets);

            using CancellationTokenSource source = new();
            UniTask preloading = preloader.PreloadAsync(_progress, source.Token);

            Assert.IsFalse(_assets.LastToken.IsCancellationRequested,
                "nothing has been cancelled yet");

            source.Cancel();

            Assert.IsTrue(_assets.LastToken.IsCancellationRequested,
                "cancelling boot has to cancel the token the provider is actually holding");

            Assert.Catch<OperationCanceledException>(() => WaitFor(preloading),
                "and the fetch has to end rather than sit there");
        }

        [Test]
        public void PreloadAsync_WhenALabelStalls_GivesUpAndSurfacesATypedFailure()
        {
            // The boot-time twin of the container's stall. A preload that fails returns and the
            // bootstrapper reports it; one that stalls returns nothing at all. Typed under
            // ChestGameException so that catch can report it the same way.
            _assets.WithDownloadSize(PRELOAD_LABEL, 4096);
            _assets.StallDownloads = true;

            DeadlinedPreloader preloader = new(
                new MinigameCatalog(new List<MinigameBaseSO>
                {
                    Definition<FirstMinigame>(MinigameLoadPolicy.Preload, PRELOAD_LABEL)
                }),
                _assets)
            {
                Deadline = TimeSpan.FromMilliseconds(50)
            };

            ContentDownloadTimeoutException error = Assert.Throws<ContentDownloadTimeoutException>(
                () => WaitFor(preloader.PreloadAsync(_progress, CancellationToken.None)));

            Assert.IsInstanceOf<ChestGameException>(error, "or boot could not report it as a failure");
            Assert.AreEqual(PRELOAD_LABEL, error.Label, "the fetch that gave up has to name itself");
        }

        [Test]
        public void PreloadAsync_WhenBootIsCancelled_StaysACancellationRatherThanATimeout()
        {
            // The app quitting mid-preload is not a content failure. The deadline is set far out so
            // only the caller's token can end the stall.
            _assets.WithDownloadSize(PRELOAD_LABEL, 4096);
            _assets.StallDownloads = true;

            DeadlinedPreloader preloader = new(
                new MinigameCatalog(new List<MinigameBaseSO>
                {
                    Definition<FirstMinigame>(MinigameLoadPolicy.Preload, PRELOAD_LABEL)
                }),
                _assets)
            {
                Deadline = TimeSpan.FromMinutes(5)
            };

            using CancellationTokenSource quitting = new();
            UniTask preloading = preloader.PreloadAsync(_progress, quitting.Token);
            quitting.Cancel();

            Exception error = Assert.Catch(() => WaitFor(preloading));

            Assert.IsInstanceOf<OperationCanceledException>(error,
                "a quitting app must stay a cancellation all the way out");
            Assert.IsNotInstanceOf<ChestGameException>(error,
                "or boot would report a failure to a player who is already gone");
        }

        // A protected seam rather than a settable property, the shape MinigameContainer uses, so
        // production keeps no tuning knob a test can reach into.
        private sealed class DeadlinedPreloader : MinigameContentPreloader
        {
            public DeadlinedPreloader(IMinigameCatalog catalog, IAssetProvider assets)
                : base(catalog, assets) { }

            public TimeSpan? Deadline { get; set; }

            protected override TimeSpan LabelDownloadTimeout => Deadline ?? base.LabelDownloadTimeout;
        }

        private static void WaitFor(UniTask task)
        {
            Task completing = task.AsTask();

            if (!((IAsyncResult)completing).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
            {
                Assert.Fail("PreloadAsync never finished, which is the hang the deadline exists to prevent");
            }

            completing.GetAwaiter().GetResult();
        }

        private void Preload(params MinigameBaseSO[] definitions) => Preload(CancellationToken.None, definitions);

        private void Preload(CancellationToken ct, params MinigameBaseSO[] definitions)
        {
            // The real catalog rather than a fake one, because it takes a plain list.
            MinigameContentPreloader preloader =
                new(new MinigameCatalog(new List<MinigameBaseSO>(definitions)), _assets);

            SynchronousUniTask.Complete(preloader.PreloadAsync(_progress, ct));
        }

        private TDefinition Definition<TDefinition>(MinigameLoadPolicy policy, string label)
            where TDefinition : MinigameBaseSO
        {
            TDefinition definition = ScriptableObject.CreateInstance<TDefinition>();
            _created.Add(definition);

            return definition.WithId(typeof(TDefinition).Name).WithContent(label, policy);
        }

        // Two concrete types, because MinigameCatalog indexes by container type.
        private abstract class PreloadableMinigameSO : MinigameBaseSO
        {
            // Never called: the preloader reads the descriptor and nothing else.
            public override MinigameContainer GetMinigameContainer() => null;
        }

        private class FirstMinigame : PreloadableMinigameSO
        {
            public override Type ContainerType => typeof(FirstMinigame);
        }

        private class SecondMinigame : PreloadableMinigameSO
        {
            public override Type ContainerType => typeof(SecondMinigame);
        }

        private class RecordingProgress : IProgress<float>
        {
            public List<float> Reported { get; } = new();

            public void Report(float value) => Reported.Add(value);
        }
    }
}
