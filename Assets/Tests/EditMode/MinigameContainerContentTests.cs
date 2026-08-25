using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Tests.Common;
using Cysharp.Threading.Tasks;
using Company.ChestGame.Minigame;
using NUnit.Framework;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.TestTools;
using VContainer;
// System brings a second Object with it; the alias keeps every use of UnityEngine's meaning what it
// did.
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.EditMode
{
    // The minigame framework's content handling: a definition asset carries AssetReferences, so
    // nothing resolves while the container is built and everything content-shaped happens together
    // in BeginAsync. Edit mode against FakeAssetProvider, which hands back already-completed tasks,
    // so BeginAsync runs inside the call and can be asserted the moment it returns.
    public class MinigameContainerContentTests
    {
        private const string VIEW_GUID = "11111111111111111111111111111111";
        private const string CONFIG_GUID = "22222222222222222222222222222222";
        private const string CONTENT_LABEL = "minigame.configurable";

        // Short enough that a test costs milliseconds rather than the ninety seconds the game ships
        // with, long enough that it cannot fire before the start it bounds has begun.
        private static readonly TimeSpan SHORT_DEADLINE = TimeSpan.FromMilliseconds(50);

        // Longer than any test run, so a test about caller cancellation can be sure the deadline is
        // not what ended the wait.
        private static readonly TimeSpan UNREACHABLE_DEADLINE = TimeSpan.FromMinutes(5);

        // How long a test waits for a bounded operation: more than SHORT_DEADLINE so a slow machine
        // is not a failure, far less than the shipped budget so a deadline that never fires fails
        // rather than hangs.
        private static readonly TimeSpan WAIT_LIMIT = TimeSpan.FromSeconds(10);

        private readonly List<Object> _created = new();

        private FakeAssetProvider _assets;
        private IObjectResolver _resolver;
        private AssetReferenceGameObject _viewRef;
        private AssetReference _configRef;
        private GameObject _parent;

        [SetUp]
        public void SetUp()
        {
            _assets = new FakeAssetProvider();

            ContainerBuilder builder = new();
            builder.RegisterInstance<IAssetProvider>(_assets);
            builder.Register<IRandomProvider, UnityRandomProvider>(Lifetime.Singleton);
            _resolver = builder.Build();

            // A GUID string is all an AssetReference is, so a test needs no real asset behind one.
            _viewRef = new AssetReferenceGameObject(VIEW_GUID);
            _configRef = new AssetReference(CONFIG_GUID);

            _parent = Track(new GameObject("MinigameParent"));
        }

        [TearDown]
        public void TearDown()
        {
            _resolver?.Dispose();

            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }
            _created.Clear();
        }

        [Test]
        public void BeginAsync_LoadsTheViewAndTheMinigamesOwnContent()
        {
            ConfigurableMinigameSO definition = Definition();
            MinigameContainer minigame = Build(definition);

            SynchronousUniTask.Complete(minigame.BeginAsync(_parent.transform, CancellationToken.None));

            CollectionAssert.AreEqual(new AssetReference[] { _viewRef, _configRef }, _assets.RequestedReferences,
                "the view and the minigame's own content are both fetched by Begin, the view first");

            // That construction resolves nothing is pinned next door, by
            // ChestsMinigameConfigTests.ADefinitionWithNoConfigDocument_FailsWithATypedException:
            // it builds a container before expecting the failure, so a config check moved back into
            // GetMinigameContainer would throw too early.
        }

        [Test]
        public void BeginAsync_ConfiguresTheControllerBeforeInjectingIt()
        {
            // A framework contract and the reason this fixture exists: a controller has to build
            // state from its own config and still be injected on top of it. ChestsMinigameSO
            // depends on exactly this.
            ConfigurableMinigameSO definition = Definition();
            MinigameContainer minigame = Build(definition);

            SynchronousUniTask.Complete(minigame.BeginAsync(_parent.transform, CancellationToken.None));

            ConfigurableController controller = (ConfigurableController)minigame.ControllerInstance;

            Assert.IsTrue(controller.Configured, "the ConfigureControllerAsync hook should have run");
            Assert.IsTrue(controller.Injected, "the controller should still be injected");
            Assert.IsTrue(controller.WasConfiguredBeforeInject,
                "Configure has to land before Inject, or a controller cannot build state from its own config");
            Assert.AreEqual(1, controller.InjectCalls, "and injected exactly once");
        }

        [Test]
        public void End_ReleasesTheViewAndTheMinigamesOwnContent()
        {
            ConfigurableMinigameSO definition = Definition();
            MinigameContainer minigame = Build(definition);
            SynchronousUniTask.Complete(minigame.BeginAsync(_parent.transform, CancellationToken.None));

            // The view instance is taken out first, because Object.Destroy is a logged error in
            // edit mode. Releasing the handles does not depend on the instance surviving; End
            // against a live view is the play-mode fixture's job.
            Object.DestroyImmediate(minigame.ViewInstance.gameObject);

            minigame.End();

            CollectionAssert.AreEquivalent(new AssetReference[] { _viewRef, _configRef }, _assets.ReleasedReferences,
                "everything Begin loaded has to be let go of, or the bundle stays resident forever");
        }

        [Test]
        public void End_OnAContainerThatNeverBegan_ReleasesNothing()
        {
            // The teardown paths call End unconditionally, so it has to be safe on a container
            // holding no handle.
            ConfigurableMinigameSO definition = Definition();
            MinigameContainer minigame = Build(definition);

            Assert.DoesNotThrow(() => minigame.End());
            CollectionAssert.IsEmpty(_assets.ReleasedReferences);
        }

        [Test]
        public void BeginAsync_WhenTheContentCannotBeLoaded_SurfacesTheTypedFailure()
        {
            // Typed all the way out, so a caller can tell "this minigame never shipped" from an
            // unrelated NullReferenceException inside Begin.
            ConfigurableMinigameSO definition = Definition();
            MinigameContainer minigame = Build(definition);
            _assets.FailWith = new MissingAssetException("Minigames/Chests/View", nameof(GameObject));

            MissingAssetException error = Assert.Throws<MissingAssetException>(() =>
                SynchronousUniTask.Complete(minigame.BeginAsync(_parent.transform, CancellationToken.None)));

            Assert.AreEqual("Minigames/Chests/View", error.AssetPath);
            Assert.IsFalse(minigame.Running, "a minigame whose content never arrived is not running");
        }

        [Test]
        public void BeginAsync_WhenALaterLoadFails_ReleasesWhatItAlreadyTook()
        {
            // The one leak nothing else could close: End is a no-op until _running is true, the
            // last line of BeginAsync. The view load succeeds and the config load fails, the only
            // arrangement that reaches the state where something is held and the start is over.
            ConfigurableMinigameSO definition = Definition();
            MinigameContainer minigame = Build(definition);
            _assets.FailingOn(_configRef, new MissingAssetException("Minigames/Chests/Config", nameof(TextAsset)));

            Assert.Throws<MissingAssetException>(() =>
                SynchronousUniTask.Complete(minigame.BeginAsync(_parent.transform, CancellationToken.None)));

            CollectionAssert.Contains(_assets.ReleasedReferences, _viewRef,
                "the view had already arrived when the config failed, and nothing else can ever let it go");
            Assert.IsFalse(minigame.Running);
        }

        [Test]
        public void BeginAsync_ForAnOnDemandMinigame_FetchesItsContentBeforeLoadingAnyOfIt()
        {
            // The other half of the load policy: on-demand content is asked for at the one moment
            // the game knows it is about to be needed, and it has to come down before the first
            // LoadAsync.
            ConfigurableMinigameSO definition = Definition();
            definition.WithContent(CONTENT_LABEL, MinigameLoadPolicy.OnDemand);
            _assets.WithDownloadSize(CONTENT_LABEL, 4096);

            SynchronousUniTask.Complete(Build(definition).BeginAsync(_parent.transform, CancellationToken.None));

            CollectionAssert.AreEqual(new[] { CONTENT_LABEL }, _assets.SizedLabels);
            CollectionAssert.AreEqual(new[] { CONTENT_LABEL }, _assets.DownloadedLabels);
        }

        [Test]
        public void BeginAsync_ForAnOnDemandMinigameWhoseContentIsAlreadyThere_DownloadsNothing()
        {
            // Zero is the answer on every run after the first, and on every build that shipped the
            // content local. Fetching anyway would put a network call in front of a button press
            // that needed none.
            ConfigurableMinigameSO definition = Definition();
            definition.WithContent(CONTENT_LABEL, MinigameLoadPolicy.OnDemand);

            SynchronousUniTask.Complete(Build(definition).BeginAsync(_parent.transform, CancellationToken.None));

            CollectionAssert.AreEqual(new[] { CONTENT_LABEL }, _assets.SizedLabels, "it still has to ask");
            CollectionAssert.IsEmpty(_assets.DownloadedLabels);
        }

        [Test]
        public void BeginAsync_ForAPreloadedMinigame_AsksAboutNoDownloadAtAll()
        {
            // Preloaded content was already fetched, so measuring it again at start would be a wait
            // the policy exists to have paid.
            ConfigurableMinigameSO definition = Definition();
            definition.WithContent(CONTENT_LABEL, MinigameLoadPolicy.Preload);
            _assets.WithDownloadSize(CONTENT_LABEL, 4096);

            SynchronousUniTask.Complete(Build(definition).BeginAsync(_parent.transform, CancellationToken.None));

            CollectionAssert.IsEmpty(_assets.SizedLabels);
            CollectionAssert.IsEmpty(_assets.DownloadedLabels);
        }

        [Test]
        public void BeginAsync_ForAnOnDemandMinigameWithNoContentLabel_DownloadsNothing()
        {
            // A blank label is not a key, the same rule the catalogs and the preloader apply. A
            // minigame naming no content is a real case, but it is warned about rather than skipped
            // in silence.
            LogAssert.Expect(LogType.Warning, new Regex("names no content label"));

            ConfigurableMinigameSO definition = Definition();
            definition.WithContent("  ", MinigameLoadPolicy.OnDemand);

            SynchronousUniTask.Complete(Build(definition).BeginAsync(_parent.transform, CancellationToken.None));

            CollectionAssert.IsEmpty(_assets.SizedLabels);
            CollectionAssert.IsEmpty(_assets.DownloadedLabels);
        }

        [Test]
        public void BeginAsync_OnAContainerAlreadyRunning_RefusesRatherThanLoadingTwice()
        {
            // One release per load is what the provider counts, and End releases exactly once, so a
            // second start would take a ref-count nothing could give back.
            ConfigurableMinigameSO definition = Definition();
            MinigameContainer minigame = Build(definition);

            SynchronousUniTask.Complete(minigame.BeginAsync(_parent.transform, CancellationToken.None));
            Assert.IsTrue(minigame.Running, "the first start has to have succeeded for this to mean anything");

            int loadsAfterTheFirstStart = _assets.RequestedReferences.Count;

            Assert.Throws<MinigameAlreadyRunningException>(
                () => SynchronousUniTask.Complete(minigame.BeginAsync(_parent.transform, CancellationToken.None)),
                "a second start must be refused, and refused with something the project can catch");

            Assert.AreEqual(loadsAfterTheFirstStart, _assets.RequestedReferences.Count,
                "the refused start must not have loaded anything a second time");

            Object.DestroyImmediate(minigame.ViewInstance.gameObject);
        }

        [Test]
        public void BeginAsync_WhenTheDownloadFails_SurfacesTheTypedFailureAndStartsNothing()
        {
            // What the shell turns into a popup. A start that could not fetch its content must not
            // leave a half-built minigame behind for the next press.
            ConfigurableMinigameSO definition = Definition();
            definition.WithContent(CONTENT_LABEL, MinigameLoadPolicy.OnDemand);
            _assets.WithDownloadSize(CONTENT_LABEL, 4096);
            _assets.FailDownloadWith = new AssetLoadException(CONTENT_LABEL, new System.Exception("offline"));

            MinigameContainer minigame = Build(definition);

            Assert.Throws<AssetLoadException>(() =>
                SynchronousUniTask.Complete(minigame.BeginAsync(_parent.transform, CancellationToken.None)));

            Assert.IsFalse(minigame.Running);
            CollectionAssert.IsEmpty(_assets.RequestedReferences, "nothing is loaded before its bundle is there");
        }

        // The two tests below cannot use SynchronousUniTask: a real deadline is a real timer on a
        // background thread, so BeginAsync is genuinely pending when it returns. Nothing that
        // completes it needs the main thread, so blocking here cannot deadlock, and GetResult
        // rethrows the original exception rather than an AggregateException.
        private static void WaitFor(UniTask task)
        {
            Task completing = task.AsTask();

            if (!((IAsyncResult)completing).AsyncWaitHandle.WaitOne(WAIT_LIMIT))
            {
                Assert.Fail("BeginAsync never finished, which is the hang the deadline exists to prevent");
            }

            completing.GetAwaiter().GetResult();
        }

        [Test]
        public void BeginAsync_WhenTheDownloadStalls_GivesUpAndSurfacesATypedFailure()
        {
            // The failure the deadline exists for, and the one the other download test cannot
            // reach: a request that fails at least returns. Typed under ChestGameException because
            // that is what GameManager catches to raise the popup.
            ConfigurableMinigameSO definition = Definition();
            definition.WithContent(CONTENT_LABEL, MinigameLoadPolicy.OnDemand);
            _assets.WithDownloadSize(CONTENT_LABEL, 4096);
            _assets.StallDownloads = true;

            ConfigurableContainer minigame = (ConfigurableContainer)Build(definition);
            minigame.Deadline = SHORT_DEADLINE;

            ContentDownloadTimeoutException error = Assert.Throws<ContentDownloadTimeoutException>(
                () => WaitFor(minigame.BeginAsync(_parent.transform, CancellationToken.None)));

            Assert.IsInstanceOf<ChestGameException>(error, "or the shell would never turn it into a popup");
            Assert.AreEqual(CONTENT_LABEL, error.Label, "the fetch that gave up has to name itself");
            Assert.IsFalse(minigame.Running, "a minigame whose content never arrived is not running");
            CollectionAssert.IsEmpty(_assets.RequestedReferences, "nothing is loaded before its bundle is there");
        }

        [Test]
        public void BeginAsync_WhenTheCallerCancels_StaysACancellationRatherThanBecomingAPlayerFacingFailure()
        {
            // The other half of the deadline, and the half that is easy to lose: a linked source
            // cancels the same way whichever end fired it, so a naive implementation turns the
            // scene going away into a popup on a scene that is going away. The deadline here is set
            // beyond any test run.
            ConfigurableMinigameSO definition = Definition();
            definition.WithContent(CONTENT_LABEL, MinigameLoadPolicy.OnDemand);
            _assets.WithDownloadSize(CONTENT_LABEL, 4096);
            _assets.StallDownloads = true;

            ConfigurableContainer minigame = (ConfigurableContainer)Build(definition);
            minigame.Deadline = UNREACHABLE_DEADLINE;

            using CancellationTokenSource caller = new();

            UniTask starting = minigame.BeginAsync(_parent.transform, caller.Token);
            caller.Cancel();

            OperationCanceledException error =
                Assert.Catch<OperationCanceledException>(() => WaitFor(starting));

            Assert.IsNotInstanceOf<ChestGameException>(error,
                "a scene going away is not a delivery failure, and the player must not be told about it");
            Assert.IsFalse(minigame.Running);
        }

        private ConfigurableMinigameSO Definition()
        {
            ConfigurableMinigameSO definition = Track(ScriptableObject.CreateInstance<ConfigurableMinigameSO>())
                .WithViewReference(_viewRef);
            definition.ConfigRef = _configRef;

            _assets.With(_viewRef, ViewPrefab());

            return definition;
        }

        private MinigameContainer Build(MinigameBaseSO definition)
        {
            MinigameContainer minigame = definition.GetMinigameContainer();
            _resolver.Inject(minigame);

            return minigame;
        }

        private GameObject ViewPrefab()
        {
            GameObject prefab = Track(new GameObject("ViewPrefab"));
            prefab.AddComponent<ConfigurableView>();

            return prefab;
        }

        private T Track<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }

        // Built on the generic base rather than MinigameBaseSO, so the hook and its ordering run
        // through the real GetMinigameContainer and the real BeginAsync.
        private class ConfigurableMinigameSO
            : MinigameBase<ConfigurableController, ConfigurableView, ConfigurableContainer>
        {
            public AssetReference ConfigRef { get; set; }

            protected override async UniTask ConfigureControllerAsync(
                ConfigurableController controller, IAssetProvider assets, CancellationToken ct)
            {
                await assets.LoadAsync<TextAsset>(ConfigRef, ct);
                controller.Configure();
            }

            public override void ReleaseContent(IAssetProvider assets) => assets.Release(ConfigRef);
        }

        // The seam a real minigame would use: a container subclass decides its own download budget.
        // Null leaves the shipped value alone.
        private class ConfigurableContainer : MinigameContainer
        {
            public TimeSpan? Deadline { get; set; }

            protected override TimeSpan ContentDownloadTimeout => Deadline ?? base.ContentDownloadTimeout;
        }

        private class ConfigurableView : MinigameViewBase
        {
            public override void SetController(MinigameControllerBase controller) { }
        }

        private class ConfigurableController : MinigameControllerBase
        {
            public bool Configured { get; private set; }
            public bool Injected => InjectCalls > 0;
            public int InjectCalls { get; private set; }
            public bool WasConfiguredBeforeInject { get; private set; }

            public void Configure() => Configured = true;

            [Inject]
            public void Inject(IRandomProvider random)
            {
                if (InjectCalls == 0) WasConfiguredBeforeInject = Configured;
                InjectCalls++;
            }

            public override void NewGame() { }

            public override void Dispose() { }
        }
    }
}
