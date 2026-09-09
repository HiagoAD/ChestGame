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
    // GameLifetimeScope.OnApplicationPause/OnApplicationQuit, proven without ever letting Awake()
    // run for real. Awake() calls base.Awake(), which VContainer uses to invoke Configure(), which
    // hardcodes RegisterCoreServices(builder, status) with no override for currencySaveInputs or
    // legacyCurrencyPlayerPrefsKey - the production call site, deliberately not overridable (see
    // docs/saving.md, "Redirecting this composition away from a developer's real save": production
    // is meant to use the real values). That means any test that lets a real GameLifetimeScope
    // Awake() would touch the developer's real Application.persistentDataPath and real legacy
    // PlayerPrefs entry - see this gate's report for where that already happens today
    // (GameBootstrapperTests, by booting the real scenes).
    //
    // This fixture avoids that entirely: the GameObject is left inactive for its whole life, which
    // defers Awake() indefinitely (Unity never calls Awake on a component whose GameObject has not
    // yet been active), so the container is never built and Configure() never runs. The private
    // _saveFlushRegistry field - the only state either callback touches - is set directly through
    // reflection to a real SaveFlushRegistry holding a scheduler built over an isolated in-memory
    // FakeSaveStore, and the private OnApplicationPause/OnApplicationQuit methods are invoked the
    // same way, since nothing in this process actually pauses or quits the application to call them
    // for us.
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

        [Test]
        public void GuardSetup_NeverRanAwake()
        {
            // If this fails, Awake() ran and the rest of this fixture's isolation claim is false -
            // Container being null is exactly what "Configure() never ran" looks like from outside.
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

        [Test]
        public void OnApplicationPause_True_WhenAFlushableThrows_LogsRatherThanPropagating()
        {
            (ISaveService service, FakeSaveStore _) = NewIsolatedService();
            SaveScheduler<CurrencySaveDocument> scheduler = new(service, Key, new FakeGameClock());
            SaveFlushRegistry registry = new();
            registry.Register(scheduler);
            // A disposed scheduler's own FlushBlocking throws SchedulerDisposed - a real exception
            // from the real type, not a fake standing in for one. Disposing after registering:
            // Register itself asserts CanFlushBlocking, which disposal does not change.
            scheduler.Dispose();
            SetRegistry(registry);

            // Logged by SaveFlushRegistry.FlushAll's own per-item catch, not by this callback's
            // outer one - FlushAll never lets a single flushable's failure reach here at all - and
            // naming the key, which is the whole reason ISaveFlushable carries one.
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
