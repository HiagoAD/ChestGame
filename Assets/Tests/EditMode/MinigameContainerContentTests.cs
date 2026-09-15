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
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Exercises how a minigame's content resolves: nothing loads while the container is built, and
    /// everything content-shaped happens together in <see cref="MinigameContainer.BeginAsync"/>.
    /// Runs in edit mode against <see cref="FakeAssetProvider"/>.
    /// </summary>
    /// <remarks>
    /// See docs/minigames.md, "Nothing loads while the container is built".
    /// See docs/testing.md, "MinigameContainerContentTests, and its fixture choices".
    /// </remarks>
    public class MinigameContainerContentTests
    {
        private const string VIEW_GUID = "11111111111111111111111111111111";
        private const string CONFIG_GUID = "22222222222222222222222222222222";
        private const string CONTENT_LABEL = "minigame.configurable";

        /// <remarks>
        /// See docs/testing.md, "MinigameContainerContentTests, and its fixture choices".
        /// </remarks>
        private static readonly TimeSpan SHORT_DEADLINE = TimeSpan.FromMilliseconds(50);

        /// <remarks>
        /// See docs/testing.md, "MinigameContainerContentTests, and its fixture choices".
        /// </remarks>
        private static readonly TimeSpan UNREACHABLE_DEADLINE = TimeSpan.FromMinutes(5);

        /// <remarks>
        /// See docs/testing.md, "MinigameContainerContentTests, and its fixture choices".
        /// </remarks>
        private static readonly TimeSpan WAIT_LIMIT = TimeSpan.FromSeconds(10);

        private readonly List<Object> _created = new();

        private FakeAssetProvider _assets;
        private IObjectResolver _resolver;
        private AssetReferenceGameObject _viewRef;
        private AssetReference _configRef;
        private GameObject _parent;

        /// <remarks>
        /// See docs/minigames.md, "A definition names its content, it does not hold it".
        /// </remarks>
        [SetUp]
        public void SetUp()
        {
            _assets = new FakeAssetProvider();

            ContainerBuilder builder = new();
            builder.RegisterInstance<IAssetProvider>(_assets);
            builder.Register<IRandomProvider, UnityRandomProvider>(Lifetime.Singleton);
            _resolver = builder.Build();

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

        /// <remarks>
        /// See docs/minigames.md, "Nothing loads while the container is built".
        /// </remarks>
        [Test]
        public void BeginAsync_LoadsTheViewAndTheMinigamesOwnContent()
        {
            ConfigurableMinigameSO definition = Definition();
            MinigameContainer minigame = Build(definition);

            SynchronousUniTask.Complete(minigame.BeginAsync(_parent.transform, CancellationToken.None));

            CollectionAssert.AreEqual(new AssetReference[] { _viewRef, _configRef }, _assets.RequestedReferences,
                "the view and the minigame's own content are both fetched by Begin, the view first");
        }

        /// <remarks>
        /// See docs/minigames.md, "Nothing loads while the container is built".
        /// </remarks>
        [Test]
        public void BeginAsync_ConfiguresTheControllerBeforeInjectingIt()
        {
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

        /// <remarks>
        /// See docs/testing.md, "MinigameContainerContentTests, and its fixture choices".
        /// </remarks>
        [Test]
        public void End_ReleasesTheViewAndTheMinigamesOwnContent()
        {
            ConfigurableMinigameSO definition = Definition();
            MinigameContainer minigame = Build(definition);
            SynchronousUniTask.Complete(minigame.BeginAsync(_parent.transform, CancellationToken.None));

            Object.DestroyImmediate(minigame.ViewInstance.gameObject);

            minigame.End();

            CollectionAssert.AreEquivalent(new AssetReference[] { _viewRef, _configRef }, _assets.ReleasedReferences,
                "everything Begin loaded has to be let go of, or the bundle stays resident forever");
        }

        /// <remarks>
        /// See docs/minigames.md, "Teardown".
        /// </remarks>
        [Test]
        public void End_OnAContainerThatNeverBegan_ReleasesNothing()
        {
            ConfigurableMinigameSO definition = Definition();
            MinigameContainer minigame = Build(definition);

            Assert.DoesNotThrow(() => minigame.End());
            CollectionAssert.IsEmpty(_assets.ReleasedReferences);
        }

        /// <remarks>
        /// See docs/architecture.md, "Exception hierarchy".
        /// </remarks>
        [Test]
        public void BeginAsync_WhenTheContentCannotBeLoaded_SurfacesTheTypedFailure()
        {
            ConfigurableMinigameSO definition = Definition();
            MinigameContainer minigame = Build(definition);
            _assets.FailWith = new MissingAssetException("Minigames/Chests/View", nameof(GameObject));

            MissingAssetException error = Assert.Throws<MissingAssetException>(() =>
                SynchronousUniTask.Complete(minigame.BeginAsync(_parent.transform, CancellationToken.None)));

            Assert.AreEqual("Minigames/Chests/View", error.AssetPath);
            Assert.IsFalse(minigame.Running, "a minigame whose content never arrived is not running");
        }

        /// <remarks>
        /// See docs/minigames.md, "Failure during a start".
        /// </remarks>
        [Test]
        public void BeginAsync_WhenALaterLoadFails_ReleasesWhatItAlreadyTook()
        {
            ConfigurableMinigameSO definition = Definition();
            MinigameContainer minigame = Build(definition);
            _assets.FailingOn(_configRef, new MissingAssetException("Minigames/Chests/Config", nameof(TextAsset)));

            Assert.Throws<MissingAssetException>(() =>
                SynchronousUniTask.Complete(minigame.BeginAsync(_parent.transform, CancellationToken.None)));

            CollectionAssert.Contains(_assets.ReleasedReferences, _viewRef,
                "the view had already arrived when the config failed, and nothing else can ever let it go");
            Assert.IsFalse(minigame.Running);
        }

        /// <remarks>
        /// See docs/content-delivery.md, "When content arrives".
        /// </remarks>
        [Test]
        public void BeginAsync_ForAnOnDemandMinigame_FetchesItsContentBeforeLoadingAnyOfIt()
        {
            ConfigurableMinigameSO definition = Definition();
            definition.WithContent(CONTENT_LABEL, MinigameLoadPolicy.OnDemand);
            _assets.WithDownloadSize(CONTENT_LABEL, 4096);

            SynchronousUniTask.Complete(Build(definition).BeginAsync(_parent.transform, CancellationToken.None));

            CollectionAssert.AreEqual(new[] { CONTENT_LABEL }, _assets.SizedLabels);
            CollectionAssert.AreEqual(new[] { CONTENT_LABEL }, _assets.DownloadedLabels);
        }

        /// <remarks>
        /// See docs/content-delivery.md, "When content arrives".
        /// </remarks>
        [Test]
        public void BeginAsync_ForAnOnDemandMinigameWhoseContentIsAlreadyThere_DownloadsNothing()
        {
            ConfigurableMinigameSO definition = Definition();
            definition.WithContent(CONTENT_LABEL, MinigameLoadPolicy.OnDemand);

            SynchronousUniTask.Complete(Build(definition).BeginAsync(_parent.transform, CancellationToken.None));

            CollectionAssert.AreEqual(new[] { CONTENT_LABEL }, _assets.SizedLabels, "it still has to ask");
            CollectionAssert.IsEmpty(_assets.DownloadedLabels);
        }

        /// <remarks>
        /// See docs/content-delivery.md, "When content arrives".
        /// </remarks>
        [Test]
        public void BeginAsync_ForAPreloadedMinigame_AsksAboutNoDownloadAtAll()
        {
            ConfigurableMinigameSO definition = Definition();
            definition.WithContent(CONTENT_LABEL, MinigameLoadPolicy.Preload);
            _assets.WithDownloadSize(CONTENT_LABEL, 4096);

            SynchronousUniTask.Complete(Build(definition).BeginAsync(_parent.transform, CancellationToken.None));

            CollectionAssert.IsEmpty(_assets.SizedLabels);
            CollectionAssert.IsEmpty(_assets.DownloadedLabels);
        }

        /// <remarks>
        /// See docs/content-delivery.md, "When content arrives".
        /// </remarks>
        [Test]
        public void BeginAsync_ForAnOnDemandMinigameWithNoContentLabel_DownloadsNothing()
        {
            LogAssert.Expect(LogType.Warning, new Regex("names no content label"));

            ConfigurableMinigameSO definition = Definition();
            definition.WithContent("  ", MinigameLoadPolicy.OnDemand);

            SynchronousUniTask.Complete(Build(definition).BeginAsync(_parent.transform, CancellationToken.None));

            CollectionAssert.IsEmpty(_assets.SizedLabels);
            CollectionAssert.IsEmpty(_assets.DownloadedLabels);
        }

        /// <remarks>
        /// See docs/minigames.md, "Starting twice is loud".
        /// </remarks>
        [Test]
        public void BeginAsync_OnAContainerAlreadyRunning_RefusesRatherThanLoadingTwice()
        {
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

        /// <remarks>
        /// See docs/content-delivery.md, "When content arrives".
        /// See docs/minigames.md, "Failure during a start".
        /// </remarks>
        [Test]
        public void BeginAsync_WhenTheDownloadFails_SurfacesTheTypedFailureAndStartsNothing()
        {
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

        /// <summary>
        /// Blocks the calling thread for up to <see cref="WAIT_LIMIT"/> for <paramref name="task"/>
        /// to complete, then rethrows the original exception rather than an
        /// <see cref="AggregateException"/>, or fails the test if it never completes.
        /// </summary>
        /// <remarks>
        /// See docs/testing.md, "MinigameContainerContentTests, and its fixture choices".
        /// </remarks>
        private static void WaitFor(UniTask task)
        {
            Task completing = task.AsTask();

            if (!((IAsyncResult)completing).AsyncWaitHandle.WaitOne(WAIT_LIMIT))
            {
                Assert.Fail("BeginAsync never finished, which is the hang the deadline exists to prevent");
            }

            completing.GetAwaiter().GetResult();
        }

        /// <remarks>
        /// See docs/content-delivery.md, "Timeouts".
        /// </remarks>
        [Test]
        public void BeginAsync_WhenTheDownloadStalls_GivesUpAndSurfacesATypedFailure()
        {
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

        /// <remarks>
        /// See docs/content-delivery.md, "Which token fired".
        /// </remarks>
        [Test]
        public void BeginAsync_WhenTheCallerCancels_StaysACancellationRatherThanBecomingAPlayerFacingFailure()
        {
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

        /// <remarks>
        /// See docs/testing.md, "MinigameContainerContentTests, and its fixture choices".
        /// </remarks>
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

        /// <remarks>
        /// See docs/content-delivery.md, "Timeouts".
        /// </remarks>
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
