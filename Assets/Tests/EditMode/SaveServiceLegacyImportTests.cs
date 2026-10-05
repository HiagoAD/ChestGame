using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="ILegacyImport"/>'s ordering guarantee: <c>Import()</c> produces a document,
    /// <c>SaveAsync</c> writes and durably persists it, and only then does <c>Clear()</c> run - never
    /// the reverse, and a failure to <c>Clear()</c> must never cost the load or the imported data.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The legacy import", and docs/testing.md, "The save fixtures".
    /// </remarks>
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

        /// <remarks>
        /// See docs/saving.md, "The legacy import", and docs/testing.md, "The save fixtures".
        /// </remarks>
        [Test]
        public void LoadAsync_WhenClearThrows_DoesNotFailTheLoad_AndDoesNotLoseTheImportedData()
        {
            _legacyImport.Present = true;
            _legacyImport.ImportFunc = () => JObject.Parse(@"{""Value"":55}");
            _legacyImport.ClearThrows = true;

            LogAssert.Expect(LogType.Error, "Failed to clear the legacy save under 'profile' after importing it: FakeLegacyImport.Clear was configured to fail");

            TestState result = null;
            Assert.DoesNotThrow(() =>
                result = SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None)));

            Assert.AreEqual(55, result.Value);
            Assert.IsTrue(SynchronousUniTask.Result(_store.ExistsAsync(Key, CancellationToken.None)),
                "a failed Clear() must be swallowed as best-effort, leaving the already-durable save intact");
        }

        /// <remarks>
        /// See docs/saving.md, "The legacy import".
        /// </remarks>
        [Test]
        public void LoadAsync_CalledTwice_WithIsPresentStillTrueTheSecondTime_ImportsOnlyOnce_AndDoesNotOverwriteANewerSave()
        {
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

        /// <remarks>
        /// See docs/saving.md, "The legacy import".
        /// </remarks>
        [Test]
        public void LoadAsync_CancelledBeforeTheWriteCompletes_ThrowsAndWritesNothing()
        {
            using CancellationTokenSource cancellation = new();
            _legacyImport.Present = true;
            _legacyImport.ImportFunc = () =>
            {
                cancellation.Cancel();
                return JObject.Parse(@"{""Value"":1}");
            };

            Assert.Throws<System.OperationCanceledException>(() =>
                SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, cancellation.Token)));

            Assert.IsFalse(SynchronousUniTask.Result(_store.ExistsAsync(Key, CancellationToken.None)),
                "cancellation before the write completes must leave nothing durable under the key");
            Assert.AreEqual(0, _legacyImport.ClearCallCount, "Clear() must never run if the write never completed");
        }

        /// <remarks>
        /// See docs/saving.md, "The legacy import".
        /// </remarks>
        [Test]
        public void LoadAsync_CancelledDuringClear_AfterTheWriteAlreadySucceeded_StillReturnsTheImportedValue()
        {
            using CancellationTokenSource cancellation = new();
            _legacyImport.Present = true;
            _legacyImport.ImportFunc = () => JObject.Parse(@"{""Value"":42}");
            _legacyImport.OnClear = () => cancellation.Cancel();

            TestState result = SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, cancellation.Token));

            Assert.AreEqual(42, result.Value, "cancellation after the durable write must not lose the imported value");
            Assert.IsTrue(SynchronousUniTask.Result(_store.ExistsAsync(Key, CancellationToken.None)));
        }

        [Test]
        public void LoadAsync_ForAKeyThisImportDoesNotTarget_NeverAsksItAnything()
        {
            _legacyImport.Present = true;
            _legacyImport.ImportFunc = () => JObject.Parse(@"{""Value"":123}");

            TestState result = SynchronousUniTask.Result(_service.LoadAsync<TestState>("some-other-key", CancellationToken.None));

            Assert.AreEqual(0, result.Value, "a key this import does not target has to read as a first run, not as the imported document");
            Assert.AreEqual(0, _legacyImport.IsPresentCallCount,
                "the wrong key must not even be asked whether legacy data exists - see docs/saving.md, 'TargetKey, and the defect a second save key exposed'");
            Assert.AreEqual(0, _legacyImport.ImportCallCount);
            Assert.AreEqual(0, _legacyImport.ClearCallCount);
            Assert.IsFalse(SynchronousUniTask.Result(_store.ExistsAsync("some-other-key", CancellationToken.None)),
                "an untargeted key must not have the legacy document written under it");
        }

        /// <remarks>
        /// See docs/saving.md, "TargetKey, and the defect a second save key exposed".
        /// </remarks>
        [Test]
        public void LoadAsync_AfterAnUntargetedKeyWasLoadedFirst_TheTargetedKeyStillImports()
        {
            _legacyImport.Present = true;
            _legacyImport.ImportFunc = () => JObject.Parse(@"{""Value"":123}");

            SynchronousUniTask.Result(_service.LoadAsync<TestState>("chests", CancellationToken.None));
            TestState targeted = SynchronousUniTask.Result(_service.LoadAsync<TestState>(Key, CancellationToken.None));

            Assert.AreEqual(123, targeted.Value, "the untargeted load consumed the import the targeted key was still waiting for");
            Assert.AreEqual(1, _legacyImport.ImportCallCount);
            Assert.AreEqual(1, _legacyImport.ClearCallCount);
        }
    }
}
