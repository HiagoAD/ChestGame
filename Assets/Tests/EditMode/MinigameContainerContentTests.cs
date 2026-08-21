using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Tests.Common;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using VContainer;

namespace Company.ChestGame.Tests.EditMode
{
    // The minigame framework's content handling. A definition asset carries AssetReferences, so
    // nothing can be resolved while the container is being built, and everything content-shaped
    // happens together in BeginAsync: the view, the minigame's own content, and the
    // configure-then-inject ordering.
    //
    // Edit mode, against FakeAssetProvider, which hands back already-completed tasks — the whole of
    // BeginAsync therefore runs inside the call and can be asserted the moment it returns.
    public class MinigameContainerContentTests
    {
        private const string VIEW_GUID = "11111111111111111111111111111111";
        private const string CONFIG_GUID = "22222222222222222222222222222222";
        private const string CONTENT_LABEL = "minigame.configurable";

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

            // That construction itself resolves nothing is pinned next door, by
            // ChestsMinigameConfigTests.ADefinitionWithNoConfigDocument_FailsWithATypedException:
            // it builds a container before it expects the failure, so a config check that moved
            // back into GetMinigameContainer would throw too early and fail there.
        }

        [Test]
        public void BeginAsync_ConfiguresTheControllerBeforeInjectingIt()
        {
            // A framework contract, not an accident of ordering, and the reason this fixture
            // exists at all. A controller has to be able to build state from its own config and
            // still be injected on top of it — ChestsMinigameSO depends on exactly this, because
            // the chest list is sized from ChestCount.
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

            // The view instance is taken out first because Object.Destroy is a logged error in edit
            // mode, and End destroys it. Releasing the handles does not depend on the instance
            // surviving, which is the half being asserted here; End against a live view is the
            // play-mode fixture's job.
            Object.DestroyImmediate(minigame.ViewInstance.gameObject);

            minigame.End();

            CollectionAssert.AreEquivalent(new AssetReference[] { _viewRef, _configRef }, _assets.ReleasedReferences,
                "everything Begin loaded has to be let go of, or the bundle stays resident forever");
        }

        [Test]
        public void End_OnAContainerThatNeverBegan_ReleasesNothing()
        {
            // The teardown paths call End unconditionally, so it has to be safe on a container that
            // holds no handle — and releasing one it never took would be worse than doing nothing.
            ConfigurableMinigameSO definition = Definition();
            MinigameContainer minigame = Build(definition);

            Assert.DoesNotThrow(() => minigame.End());
            CollectionAssert.IsEmpty(_assets.ReleasedReferences);
        }

        [Test]
        public void BeginAsync_WhenTheContentCannotBeLoaded_SurfacesTheTypedFailure()
        {
            // Typed all the way out, so a caller can tell "this minigame never shipped" from an
            // unrelated NullReferenceException raised somewhere inside Begin.
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
            // The one leak nothing else could ever close. End is a no-op until _running is true,
            // and _running is the last line of BeginAsync, so without the unwind a load that threw
            // halfway would leave the view resident for the rest of the session with no handle on
            // it anywhere.
            //
            // The view load succeeds and the config load fails, which is the only arrangement that
            // reaches the state where something is held and the start is over.
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
            // The other half of the load policy. A minigame authored to arrive when it is asked for
            // is asked for here, which is the one moment the game knows it is about to be needed —
            // and it has to come down before the first LoadAsync, not alongside it.
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
            // Zero is the answer on every run after the first, and on every run of a build that
            // shipped the content local. Fetching anyway would put a network call in front of a
            // button press that needed none.
            ConfigurableMinigameSO definition = Definition();
            definition.WithContent(CONTENT_LABEL, MinigameLoadPolicy.OnDemand);

            SynchronousUniTask.Complete(Build(definition).BeginAsync(_parent.transform, CancellationToken.None));

            CollectionAssert.AreEqual(new[] { CONTENT_LABEL }, _assets.SizedLabels, "it still has to ask");
            CollectionAssert.IsEmpty(_assets.DownloadedLabels);
        }

        [Test]
        public void BeginAsync_ForAPreloadedMinigame_AsksAboutNoDownloadAtAll()
        {
            // Preloaded content was fetched before the player could press anything, so measuring it
            // again at start would be a wait the policy exists to have already paid.
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
            // minigame naming no content of its own is a real case, not an error.
            ConfigurableMinigameSO definition = Definition();
            definition.WithContent("  ", MinigameLoadPolicy.OnDemand);

            SynchronousUniTask.Complete(Build(definition).BeginAsync(_parent.transform, CancellationToken.None));

            CollectionAssert.IsEmpty(_assets.SizedLabels);
            CollectionAssert.IsEmpty(_assets.DownloadedLabels);
        }

        [Test]
        public void BeginAsync_WhenTheDownloadFails_SurfacesTheTypedFailureAndStartsNothing()
        {
            // What the shell turns into a popup. A start that could not fetch its content must not
            // leave a half-built minigame behind for the next press to find.
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

        // Built on the generic base rather than on MinigameBaseSO directly, so the hook and the
        // order it runs in are exercised through the real GetMinigameContainer and the real
        // BeginAsync, not a stand-in for either.
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

        private class ConfigurableContainer : MinigameContainer { }

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
