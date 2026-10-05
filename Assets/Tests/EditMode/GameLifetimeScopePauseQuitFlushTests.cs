using System;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Company.ChestGame.Core;
using Company.ChestGame.Currency;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="GameLifetimeScope"/>'s <c>OnApplicationPause</c>/<c>OnApplicationQuit</c>
    /// flush, without ever letting a real <c>Awake()</c> run.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "Sealing the boot path a test cannot pass arguments through".
    /// </remarks>
    public class GameLifetimeScopePauseQuitFlushTests
    {
        private const string Key = "currency";

        private GameObject _gameObject;
        private GameLifetimeScope _scope;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject(nameof(GameLifetimeScopePauseQuitFlushTests));
            _gameObject.SetActive(false);
            _scope = _gameObject.AddComponent<GameLifetimeScope>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null) UnityEngine.Object.DestroyImmediate(_gameObject);
        }

        private static FieldInfo RegistryField() =>
            typeof(GameLifetimeScope).GetField("_saveFlushRegistry", BindingFlags.Instance | BindingFlags.NonPublic);

        private static MethodInfo LifecycleMethod(string name) =>
            typeof(GameLifetimeScope).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);

        private void SetRegistry(ISaveFlushRegistry registry) =>
            RegistryField().SetValue(_scope, registry);

        private void InvokeOnApplicationPause(bool pauseStatus) =>
            LifecycleMethod("OnApplicationPause").Invoke(_scope, new object[] { pauseStatus });

        private void InvokeOnApplicationQuit() =>
            LifecycleMethod("OnApplicationQuit").Invoke(_scope, Array.Empty<object>());

        private static (ISaveService service, FakeSaveStore store) NewIsolatedService()
        {
            FakeSaveStore store = new();
            return (new SaveService(new JsonCodec(), new NoProtection(), store), store);
        }

        /// <remarks>
        /// See docs/saving.md, "Sealing the boot path a test cannot pass arguments through".
        /// </remarks>
        [Test]
        public void GuardSetup_NeverRanAwake()
        {
            Assert.IsNull(_scope.Container, "guard: Awake() must never have run in this fixture");
        }

        [Test]
        public void OnApplicationPause_True_FlushesThePendingCurrencySave()
        {
            (ISaveService service, FakeSaveStore store) = NewIsolatedService();
            SaveScheduler<CurrencySaveDocument> scheduler = new(service, Key, new FakeGameClock());
            SaveFlushRegistry registry = new();
            registry.Register(scheduler);
            SetRegistry(registry);

            scheduler.MarkDirty(new CurrencySaveDocument());
            Assert.IsTrue(scheduler.HasPendingWrite, "guard: something has to be pending for a flush to prove anything");

            InvokeOnApplicationPause(true);

            Assert.IsFalse(scheduler.HasPendingWrite, "OnApplicationPause(true) has to flush the pending save");
            Assert.IsTrue(SynchronousUniTask.Result(store.ExistsAsync(Key, CancellationToken.None)),
                "the flush has to have actually reached the store");

            scheduler.Dispose();
        }

        [Test]
        public void OnApplicationPause_False_DoesNotFlush()
        {
            (ISaveService service, FakeSaveStore _) = NewIsolatedService();
            SaveScheduler<CurrencySaveDocument> scheduler = new(service, Key, new FakeGameClock());
            SaveFlushRegistry registry = new();
            registry.Register(scheduler);
            SetRegistry(registry);

            scheduler.MarkDirty(new CurrencySaveDocument());

            InvokeOnApplicationPause(false);

            Assert.IsTrue(scheduler.HasPendingWrite, "pausing 'false' (resuming) must not flush anything");

            scheduler.Dispose();
        }

        [Test]
        public void OnApplicationQuit_FlushesThePendingCurrencySave()
        {
            (ISaveService service, FakeSaveStore store) = NewIsolatedService();
            SaveScheduler<CurrencySaveDocument> scheduler = new(service, Key, new FakeGameClock());
            SaveFlushRegistry registry = new();
            registry.Register(scheduler);
            SetRegistry(registry);

            scheduler.MarkDirty(new CurrencySaveDocument());

            InvokeOnApplicationQuit();

            Assert.IsFalse(scheduler.HasPendingWrite);
            Assert.IsTrue(SynchronousUniTask.Result(store.ExistsAsync(Key, CancellationToken.None)));

            scheduler.Dispose();
        }

        /// <remarks>
        /// See docs/saving.md, "Why the flush stopped being one field".
        /// </remarks>
        [Test]
        public void OnApplicationPause_True_WhenAFlushableThrows_LogsRatherThanPropagating()
        {
            (ISaveService service, FakeSaveStore _) = NewIsolatedService();
            SaveScheduler<CurrencySaveDocument> scheduler = new(service, Key, new FakeGameClock());
            SaveFlushRegistry registry = new();
            registry.Register(scheduler);
            scheduler.Dispose();
            SetRegistry(registry);

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape($"The save under '{Key}' failed to flush on pause/quit")));

            Assert.DoesNotThrow(() => InvokeOnApplicationPause(true));
        }

        [Test]
        public void OnApplicationQuit_WhenAFlushableThrows_LogsRatherThanPropagating()
        {
            (ISaveService service, FakeSaveStore _) = NewIsolatedService();
            SaveScheduler<CurrencySaveDocument> scheduler = new(service, Key, new FakeGameClock());
            SaveFlushRegistry registry = new();
            registry.Register(scheduler);
            scheduler.Dispose();
            SetRegistry(registry);

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape($"The save under '{Key}' failed to flush on pause/quit")));

            Assert.DoesNotThrow(() => InvokeOnApplicationQuit());
        }
    }
}
