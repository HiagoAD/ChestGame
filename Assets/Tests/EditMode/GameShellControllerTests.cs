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
        public void Dispose_EndsTheActiveMinigame()
        {
            MinigameContainer container = BuildContainer("id");
            SynchronousUniTask.Complete(_controller.StartAsync("id", _parent.transform, CancellationToken.None));
            Assert.IsTrue(container.Running, "guard: the start has to have succeeded for disposal to mean anything");

            ExpectDestroy();
            _controller.Dispose();

            Assert.IsFalse(container.Running);
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
