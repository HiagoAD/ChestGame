using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Company.ChestGame.Gameplay;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Popups;
using Company.ChestGame.Tests.Common;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.TestTools;
using VContainer;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Exercises <see cref="GameShellController"/> against <see cref="FakeMinigameManager"/> and
    /// <see cref="FakeAssetProvider"/>, so a minigame's <see cref="MinigameContainer.BeginAsync"/>
    /// runs for real.
    /// </summary>
    /// <remarks>
    /// See docs/mvc.md.
    /// </remarks>
    public class GameShellControllerTests
    {
        private static readonly Regex EditModeDestroy = new("Destroy may not be called from edit mode");

        private readonly List<Object> _created = new();
        private readonly List<MinigameContainer> _containers = new();

        private FakeAssetProvider _assets;
        private IObjectResolver _resolver;
        private FakeMinigameManager _minigames;
        private FakePopupManager _popups;
        private GameShellController _controller;
        private GameObject _parent;

        [SetUp]
        public void SetUp()
        {
            _assets = new FakeAssetProvider();

            ContainerBuilder builder = new();
            builder.RegisterInstance<IAssetProvider>(_assets);
            _resolver = builder.Build();

            _minigames = new FakeMinigameManager();
            _popups = new FakePopupManager();
            _controller = new GameShellController(_minigames, _popups);

            _parent = Track(new GameObject("ShellParent"));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (MinigameContainer container in _containers)
            {
                if (container.ViewInstance != null) Object.DestroyImmediate(container.ViewInstance.gameObject);
            }
            _containers.Clear();

            _controller.Dispose();
            _resolver?.Dispose();

            foreach (Object created in _created)
            {
                if (created != null) Object.DestroyImmediate(created);
            }
            _created.Clear();
        }

        [Test]
        public void StartAsync_StartingADifferentMinigame_EndsTheFirst()
        {
            MinigameContainer first = BuildContainer("first");
            MinigameContainer second = BuildContainer("second");

            SynchronousUniTask.Complete(_controller.StartAsync("first", _parent.transform, CancellationToken.None));
            Assert.IsTrue(first.Running, "guard: the first start has to have succeeded for this to mean anything");

            ExpectDestroy();
            SynchronousUniTask.Complete(_controller.StartAsync("second", _parent.transform, CancellationToken.None));

            Assert.IsFalse(first.Running, "starting a different minigame must end the one that was active");
            Assert.IsTrue(second.Running);
        }

        [Test]
        public void StartAsync_RestartingTheRunningMinigame_CallsNewGameWithoutRebuildingIt()
        {
            MinigameContainer container = BuildContainer("id");

            SynchronousUniTask.Complete(_controller.StartAsync("id", _parent.transform, CancellationToken.None));
            MinigameControllerBase first = container.ControllerInstance;
            Assert.IsTrue(container.Running, "guard: the first start has to have succeeded");

            SynchronousUniTask.Complete(_controller.StartAsync("id", _parent.transform, CancellationToken.None));

            Assert.AreEqual(1, _minigames.GetCalls.Count,
                "restarting the running minigame must reuse its container rather than ask for a new one");
            Assert.AreSame(first, container.ControllerInstance, "the controller must survive a restart");
            Assert.AreEqual(2, ((FakeMinigameController)first).NewGameCalls,
                "every start calls NewGame, including a restart of the minigame already running");
        }

        [Test]
        public void StartAsync_WhileAStartIsInFlight_IgnoresASecondCall()
        {
            const string label = "content.stalled";
            MinigameContainer container = BuildContainer("id",
                definition => definition.WithContent(label, MinigameLoadPolicy.OnDemand));
            _assets.WithDownloadSize(label, 4096);
            _assets.StallDownloads = true;

            UniTask starting = _controller.StartAsync("id", _parent.transform, CancellationToken.None);
            Assert.AreEqual(UniTaskStatus.Pending, starting.Status,
                "guard: the first start must still be in flight for the guard to mean anything");

            SynchronousUniTask.Complete(_controller.StartAsync("id", _parent.transform, CancellationToken.None));

            Assert.AreEqual(1, _minigames.GetCalls.Count,
                "a start already in flight must not ask the manager for anything");
            Assert.IsFalse(container.Running);
        }

        [Test]
        public void StartAsync_CancelledMidFlight_UnwindsWithoutSpawningAPopup()
        {
            const string label = "content.stalled";
            MinigameContainer container = BuildContainer("id",
                definition => definition.WithContent(label, MinigameLoadPolicy.OnDemand));
            _assets.WithDownloadSize(label, 4096);
            _assets.StallDownloads = true;

            List<bool> busyStates = new();
            _controller.OnBusyChanged += busyStates.Add;

            using CancellationTokenSource cancellation = new();
            UniTask starting = _controller.StartAsync("id", _parent.transform, cancellation.Token);
            Assert.AreEqual(UniTaskStatus.Pending, starting.Status,
                "guard: the start has to be in flight for cancelling it to mean anything");

            cancellation.Cancel();

            Assert.AreEqual(UniTaskStatus.Canceled, starting.Status,
                "a cancelled start must surface as cancellation rather than completing");
            Assert.IsEmpty(_popups.SpawnCalls,
                "cancelling a start is not a content failure, so it must not reach the player as one");
            CollectionAssert.AreEqual(new[] { true, false }, busyStates,
                "the busy flag must clear when a cancelled start unwinds, or the button stays dead");
            Assert.IsFalse(container.Running);
        }

        [Test]
        public void StartAsync_RaisesBusyThenNotBusy()
        {
            BuildContainer("id");
            List<bool> busyStates = new();
            _controller.OnBusyChanged += busyStates.Add;

            SynchronousUniTask.Complete(_controller.StartAsync("id", _parent.transform, CancellationToken.None));

            CollectionAssert.AreEqual(new[] { true, false }, busyStates);
        }

        [Test]
        public void StartAsync_WhenBeginAsyncFailsWithAChestGameException_SpawnsThePopupAndDoesNotEscape()
        {
            MinigameContainer container = BuildContainer("id");
            _assets.FailWith = new MissingAssetException("Minigames/Shell/View", nameof(GameObject));
            LogAssert.Expect(LogType.Exception, new Regex(nameof(MissingAssetException)));

            Assert.DoesNotThrow(() =>
                SynchronousUniTask.Complete(_controller.StartAsync("id", _parent.transform, CancellationToken.None)));

            Assert.AreEqual(1, _popups.SpawnCalls.Count, "the failure must spawn exactly one popup");
            Assert.AreEqual(typeof(ContentUnavailablePopup), _popups.SpawnCalls[0].popupType);
            Assert.IsInstanceOf<ContentUnavailablePopupData>(_popups.SpawnCalls[0].data);
            Assert.IsFalse(container.Running, "a minigame whose content never arrived is not running");
        }

        [Test]
        public void StartAsync_WhenDisposedWhileInFlight_CancelsWithoutAPopupAndRefusesFurtherStarts()
        {
            MinigameContainer container = BuildStalledContainer("id", downloadIgnoresCancellation: false);
            List<bool> busyStates = new();
            _controller.OnBusyChanged += busyStates.Add;

            UniTask starting = _controller.StartAsync("id", _parent.transform, CancellationToken.None);
            Assert.AreEqual(UniTaskStatus.Pending, starting.Status,
                "guard: the start has to be in flight for disposing under it to mean anything");

            _controller.Dispose();

            Assert.AreEqual(UniTaskStatus.Canceled, starting.Status,
                "disposing the controller must cancel a start still in flight, not leave it running");
            Assert.IsEmpty(_popups.SpawnCalls,
                "a start cancelled by disposal is not a content failure and must not reach the player as one");
            Assert.IsFalse(container.Running);
            CollectionAssert.AreEqual(new[] { true, false }, busyStates,
                "a start cancelled by disposal must raise the end of its busy state from inside Dispose, "
                + "before the subscribers are dropped");

            Assert.DoesNotThrow(
                () => SynchronousUniTask.Complete(_controller.StartAsync("id", _parent.transform, CancellationToken.None)),
                "a start requested after disposal must be a no-op");

            Assert.AreEqual(1, _minigames.GetCalls.Count,
                "a start requested after disposal must not ask the manager for anything");
            Assert.IsFalse(container.Running, "a disposed controller must not start a minigame");
        }

        [Test]
        public void StartAsync_WhoseContentArrivesAfterDispose_NeverBeginsTheContainer()
        {
            MinigameContainer container = BuildStalledContainer("id", downloadIgnoresCancellation: true);
            FakeMinigameController late = (FakeMinigameController)container.ControllerInstance;
            List<bool> busyStates = new();
            _controller.OnBusyChanged += busyStates.Add;

            UniTask starting = _controller.StartAsync("id", _parent.transform, CancellationToken.None);
            _controller.Dispose();
            Assert.AreEqual(UniTaskStatus.Pending, starting.Status,
                "guard: the download ignores cancellation, so the start must still be in flight after disposal");

            _assets.CompleteStalledDownloads();

            Assert.AreEqual(UniTaskStatus.Canceled, starting.Status,
                "a start that finishes after disposal must surface as cancellation rather than completing");
            Assert.IsFalse(container.Running, "a container must not begin after disposal");
            Assert.AreEqual(0, late.InjectCalls,
                "injecting the controller registers its save scheduler, which nothing would ever unregister");
            Assert.AreEqual(0, late.NewGameCalls, "a game must not begin on a disposed shell");
            Assert.IsNull(container.ViewInstance, "no view may be instantiated after disposal");
            CollectionAssert.Contains(_assets.ReleasedReferences, container.ViewRef,
                "the failed start must release the content it had just loaded");
            Assert.IsEmpty(_popups.SpawnCalls, "a start cancelled by disposal must not spawn a popup");
            CollectionAssert.AreEqual(new[] { true }, busyStates,
                "the end of a start that outlived disposal must reach nobody, because disposal dropped the subscribers");
        }

        [Test]
        public void StartAsync_WhoseContentArrivesAfterTheCallerCancelled_NeverBeginsTheContainer()
        {
            MinigameContainer container = BuildStalledContainer("id", downloadIgnoresCancellation: true);
            FakeMinigameController late = (FakeMinigameController)container.ControllerInstance;
            List<bool> busyStates = new();
            _controller.OnBusyChanged += busyStates.Add;

            using CancellationTokenSource cancellation = new();
            UniTask starting = _controller.StartAsync("id", _parent.transform, cancellation.Token);
            cancellation.Cancel();
            Assert.AreEqual(UniTaskStatus.Pending, starting.Status,
                "guard: the download ignores cancellation, so the start must still be in flight after the cancel");

            _assets.CompleteStalledDownloads();

            Assert.AreEqual(UniTaskStatus.Canceled, starting.Status,
                "a start that finishes after its caller cancelled must surface as cancellation rather than completing");
            Assert.IsFalse(container.Running, "a container must not begin for a caller that gave up");
            Assert.AreEqual(0, late.InjectCalls, "the controller must never be injected for a cancelled start");
            Assert.AreEqual(0, late.NewGameCalls, "a game must not begin for a caller that cancelled");
            Assert.IsNull(container.ViewInstance);
            CollectionAssert.Contains(_assets.ReleasedReferences, container.ViewRef,
                "the failed start must release the content it had just loaded");
            Assert.IsEmpty(_popups.SpawnCalls);
            CollectionAssert.AreEqual(new[] { true, false }, busyStates,
                "the busy flag must clear when a late-cancelled start unwinds, or the button stays dead");
        }

        [Test]
        public void StartAsync_WhenAChestGameExceptionArrivesAfterDispose_LogsItButSpawnsNoPopup()
        {
            MinigameContainer container = BuildStalledContainer("id", downloadIgnoresCancellation: true);

            UniTask starting = _controller.StartAsync("id", _parent.transform, CancellationToken.None);
            _controller.Dispose();
            Assert.AreEqual(UniTaskStatus.Pending, starting.Status,
                "guard: the download ignores cancellation, so the start must still be in flight after disposal");

            _assets.FailWith = new MissingAssetException("Minigames/Shell/View", nameof(GameObject));
            LogAssert.Expect(LogType.Exception, new Regex(nameof(MissingAssetException)));

            _assets.CompleteStalledDownloads();

            Assert.DoesNotThrow(() => SynchronousUniTask.Complete(starting),
                "a content failure is handled by the shell whether or not it is disposed");
            Assert.IsEmpty(_popups.SpawnCalls,
                "a popup spawned from a disposed shell would appear on a scene that is gone");
            Assert.IsFalse(container.Running);
        }

        [Test]
        public void Dispose_EndsTheActiveMinigame()
        {
            MinigameContainer container = BuildContainer("id");
            SynchronousUniTask.Complete(_controller.StartAsync("id", _parent.transform, CancellationToken.None));
            Assert.IsTrue(container.Running, "guard: the start has to have succeeded for disposal to mean anything");

            ExpectDestroy();
            _controller.Dispose();

            Assert.IsFalse(container.Running);
        }

        [Test]
        public void Dispose_WhenACancellationCallbackThrows_StillDropsTheBusySubscribers()
        {
            MinigameContainer container = BuildStalledContainer("id", downloadIgnoresCancellation: true);
            FakeMinigameController late = (FakeMinigameController)container.ControllerInstance;
            _assets.StalledDownloadsThrowOnCancellation = true;
            List<bool> busyStates = new();
            _controller.OnBusyChanged += busyStates.Add;

            UniTask starting = _controller.StartAsync("id", _parent.transform, CancellationToken.None);
            Assert.AreEqual(UniTaskStatus.Pending, starting.Status,
                "guard: the start has to be in flight for disposing under it to mean anything");

            AggregateException thrown = Assert.Throws<AggregateException>(() => _controller.Dispose(),
                "a cancellation callback that throws must surface from Dispose");
            Assert.IsInstanceOf<InvalidOperationException>(thrown.Flatten().InnerException,
                "guard: the exception has to be the fake's callback for this to mean anything");
            Assert.AreEqual(UniTaskStatus.Pending, starting.Status,
                "guard: the download ignores cancellation, so the start must still be in flight after disposal");

            _assets.CompleteStalledDownloads();

            Assert.AreEqual(UniTaskStatus.Canceled, starting.Status);
            Assert.IsFalse(container.Running);
            Assert.AreEqual(0, late.InjectCalls);
            Assert.AreEqual(0, late.NewGameCalls);
            Assert.IsNull(container.ViewInstance);
            CollectionAssert.Contains(_assets.ReleasedReferences, container.ViewRef);
            Assert.IsEmpty(_popups.SpawnCalls);
            CollectionAssert.AreEqual(new[] { true }, busyStates,
                "a Dispose whose cancellation threw must still have dropped the subscribers");
        }

        [Test]
        public void Dispose_CalledTwice_EndsTheActiveMinigameOnce()
        {
            MinigameContainer container = BuildContainer("id");
            SynchronousUniTask.Complete(_controller.StartAsync("id", _parent.transform, CancellationToken.None));
            Assert.IsTrue(container.Running, "guard: the start has to have succeeded for disposal to mean anything");

            ExpectDestroy();
            _controller.Dispose();

            Assert.DoesNotThrow(() => _controller.Dispose(), "disposing twice must be harmless");
            Assert.AreEqual(1, ((FakeMinigameController)container.ControllerInstance).DisposeCalls,
                "the second disposal must not end the minigame again");
        }

        private static void ExpectDestroy() => LogAssert.Expect(LogType.Error, EditModeDestroy);

        private MinigameContainer BuildContainer(string id, Action<FakeMinigameSO> configure = null)
        {
            AssetReferenceGameObject viewRef = new($"view-{id}");
            FakeMinigameSO definition = Track(FakeMinigameSO.Create(id));
            definition.ViewReference = viewRef;
            configure?.Invoke(definition);

            _assets.With(viewRef, ViewPrefab());

            MinigameContainer container = definition.GetMinigameContainer();
            _resolver.Inject(container);

            _minigames.Set(id, container);
            _containers.Add(container);
            return container;
        }

        /// <summary>
        /// Builds a container whose on-demand download never finishes on its own, ending only when
        /// its token is cancelled or, if <paramref name="downloadIgnoresCancellation"/>, when the
        /// test calls <see cref="FakeAssetProvider.CompleteStalledDownloads"/>.
        /// </summary>
        private MinigameContainer BuildStalledContainer(string id, bool downloadIgnoresCancellation)
        {
            const string label = "content.stalled";
            MinigameContainer container = BuildContainer(id,
                definition => definition.WithContent(label, MinigameLoadPolicy.OnDemand));

            _assets.WithDownloadSize(label, 4096);
            _assets.StallDownloads = true;
            _assets.StalledDownloadsIgnoreCancellation = downloadIgnoresCancellation;
            return container;
        }

        private GameObject ViewPrefab()
        {
            GameObject prefab = Track(new GameObject("ShellMinigameViewPrefab"));
            prefab.AddComponent<ShellTestMinigameView>();
            return prefab;
        }

        private T Track<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }

        private class ShellTestMinigameView : MinigameViewBase
        {
            public override void SetController(MinigameControllerBase controller)
            {
            }
        }
    }
}
