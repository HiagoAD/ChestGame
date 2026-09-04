using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // ILegacyImport's ordering guarantee (docs/saving.md, "The legacy import"): Import() produces a
    // document, SaveAsync writes and durably persists it, and only then does Clear() run - never the
    // reverse, and a failure to Clear() must never cost the load or the imported data. The real
    // JsonCodec and NoProtection run underneath (rather than the isolated fakes SaveServiceTests
    // uses) because the idempotency scenario below needs an honest round trip: whether a value saved
    // after the import is really what a second load reads back, not what a fixed fake decode result
    // says it is.
    public class SaveServiceLegacyImportTests
    {
        private const string Key = "profile";

        private FakeSaveStore _store;
        private FakeLegacyImport _legacyImport;
        private SaveService _service;

        private class TestState
        {
            public int Value;
        }

        [SetUp]
        public void SetUp()
        {
            _store = new FakeSaveStore();
            _legacyImport = new FakeLegacyImport();
            _service = new SaveService(new JsonCodec(), new NoProtection(), _store, migrator: null, legacyImport: _legacyImport);
        }

        [Test]
        public void LoadAsync_WhenLegacyIsNotPresentFromTheStart_ReturnsAFreshInstance_AndNeverCallsImport()
        {
            _legacyImport.Present = false;

            TestState result = SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None));

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Value);
            Assert.AreEqual(0, _legacyImport.ImportCallCount);
            Assert.AreEqual(0, _legacyImport.ClearCallCount);
            Assert.IsFalse(SynchronousUniTask.Result(_store.ExistsAsync(Key, CancellationToken.None)),
                "a plain first run with nothing legacy present must not write anything");
        }

        [Test]
        public void LoadAsync_WhenLegacyIsPresent_ReturnsTheImportedValue_WritesARealSave_AndCallsClear()
        {
            _legacyImport.Present = true;
            _legacyImport.ImportFunc = () => JObject.Parse(@"{""Value"":123}");

            TestState result = SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None));

            Assert.AreEqual(123, result.Value);
            Assert.AreEqual(1, _legacyImport.ImportCallCount);
            Assert.AreEqual(1, _legacyImport.ClearCallCount);
            Assert.IsTrue(SynchronousUniTask.Result(_store.ExistsAsync(Key, CancellationToken.None)),
                "a real save has to exist under the key once the import runs");
        }

        [Test]
        public void LoadAsync_WhenLegacyIsPresent_TheSaveIsDurablyWritten_BeforeClearRuns()
        {
            _legacyImport.Present = true;
            _legacyImport.ImportFunc = () => JObject.Parse(@"{""Value"":7}");
            bool storeAlreadyHadDataWhenClearRan = false;
            _legacyImport.OnClear = () =>
                storeAlreadyHadDataWhenClearRan = SynchronousUniTask.Result(_store.ExistsAsync(Key, CancellationToken.None));

            SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None));

            Assert.IsTrue(storeAlreadyHadDataWhenClearRan,
                "the write has to be durable by the time Clear() runs - that ordering is the entire reason this is three methods, not one");
        }

        [Test]
        public void LoadAsync_WhenClearThrows_DoesNotFailTheLoad_AndDoesNotLoseTheImportedData()
        {
            _legacyImport.Present = true;
            _legacyImport.ImportFunc = () => JObject.Parse(@"{""Value"":55}");
            _legacyImport.ClearThrows = true;

            TestState result = null;
            Assert.DoesNotThrow(() =>
                result = SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None)));

            Assert.AreEqual(55, result.Value);
            Assert.IsTrue(SynchronousUniTask.Result(_store.ExistsAsync(Key, CancellationToken.None)),
                "a failed Clear() must be swallowed as best-effort, leaving the already-durable save intact");
        }

        [Test]
        public void LoadAsync_CalledTwice_WithIsPresentStillTrueTheSecondTime_ImportsOnlyOnce_AndDoesNotOverwriteANewerSave()
        {
            // IsPresent() staying true simulates a Clear() that silently failed to actually remove
            // the legacy data. The structural guarantee this design exists for is that the branch
            // becomes unreachable once the store has bytes under key - not that IsPresent() is
            // trusted to answer false. A value saved after the import must survive a second load
            // untouched by the stale legacy data; overwriting it is the data-loss scenario this
            // whole design exists to prevent.
            _legacyImport.Present = true;
            _legacyImport.ImportFunc = () => JObject.Parse(@"{""Value"":1}");

            TestState first = SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None));
            Assert.AreEqual(1, first.Value);

            SynchronousUniTask.Complete(_service.SaveAsync(Key, new TestState { Value = 999 }, CancellationToken.None));

            TestState second = SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None));

            Assert.AreEqual(1, _legacyImport.ImportCallCount, "Import() must run exactly once across two loads");
            Assert.AreEqual(999, second.Value,
                "a value saved after the import must not be overwritten by stale legacy data on the second load");
        }

        // --- Cancellation before and after the durable write --------------------------------------

        [Test]
        public void LoadAsync_CancelledBeforeTheWriteCompletes_ThrowsAndWritesNothing()
        {
            using CancellationTokenSource cancellation = new();
            _legacyImport.Present = true;
            _legacyImport.ImportFunc = () =>
            {
                // The write has not started yet; cancelling here proves SaveAsync's own
                // ThrowIfCancellationRequested actually aborts the write rather than the import
                // path racing ahead of it.
                cancellation.Cancel();
                return JObject.Parse(@"{""Value"":1}");
            };

            Assert.Throws<System.OperationCanceledException>(() =>
                SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, cancellation.Token)));

            Assert.IsFalse(SynchronousUniTask.Result(_store.ExistsAsync(Key, CancellationToken.None)),
                "cancellation before the write completes must leave nothing durable under the key");
            Assert.AreEqual(0, _legacyImport.ClearCallCount, "Clear() must never run if the write never completed");
        }

        [Test]
        public void LoadAsync_CancelledDuringClear_AfterTheWriteAlreadySucceeded_StillReturnsTheImportedValue()
        {
            using CancellationTokenSource cancellation = new();
            _legacyImport.Present = true;
            _legacyImport.ImportFunc = () => JObject.Parse(@"{""Value"":42}");
            // By the time Clear() runs the write has already succeeded, so cancelling here must not
            // be able to turn a completed import into a failed load.
            _legacyImport.OnClear = () => cancellation.Cancel();

            TestState result = SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, cancellation.Token));

            Assert.AreEqual(42, result.Value, "cancellation after the durable write must not lose the imported value");
            Assert.IsTrue(SynchronousUniTask.Result(_store.ExistsAsync(Key, CancellationToken.None)));
        }
    }
}
