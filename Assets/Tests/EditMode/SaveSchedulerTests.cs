using System.Text.RegularExpressions;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// The parts of <c>SaveScheduler&lt;T&gt;</c> provable without a real thread hop: its own
    /// constructor guards, <c>CanFlushBlocking</c> answering from the <c>ISaveService</c> it was
    /// given, <c>SchedulerDisposed</c> once <c>Dispose</c> has run, and coalescing, <c>FlushAsync</c>
    /// and what a failed write must and must not do. What still needs a player loop or a real hop
    /// is in <c>SaveSchedulerPlayModeTests</c>.
    /// </summary>
    public class SaveSchedulerTests
    {
        private const string Key = "scheduled";
        private const int WindowMilliseconds = 100;
        private const int FramesPerWindow = 2;

        private class DummyState
        {
            public int Value;
        }

        private static ISaveService NewSaveService(ISaveStore store) =>
            new SaveService(new FakeSaveCodec(), new NoProtection(), store);

        /// <summary>
        /// A service over a real codec, so what landed can be read back and told apart by its Value.
        /// </summary>
        private static ISaveService NewReadableSaveService(ISaveStore store) =>
            new SaveService(new JsonCodec(), new NoProtection(), store);

        private static int StoredValue(ISaveService service) =>
            SynchronousUniTask.Result(service.LoadAsync<DummyState>(Key, CancellationToken.None)).Value;

        /// <summary>
        /// Expects the documented failure log: once per failed write, naming the key.
        /// </summary>
        private static void ExpectOneFailedWriteLog() =>
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape($"SaveScheduler for '{Key}' failed to save and will retry")));

        [Test]
        public void Constructor_WithNoSaveService_ThrowsNoSaveService()
        {
            SaveException error = Assert.Throws<SaveException>(
                () => new SaveScheduler<DummyState>(null, "key", new FakeGameClock()));
            StringAssert.Contains("needs an ISaveService", error.Message);
        }

        [Test]
        public void Constructor_WithNoClock_ThrowsNoClock()
        {
            ISaveService service = NewSaveService(new FakeSaveStore());

            SaveException error = Assert.Throws<SaveException>(
                () => new SaveScheduler<DummyState>(service, "key", null));
            StringAssert.Contains("needs an IGameClock", error.Message);
        }

        [TestCase(0)]
        [TestCase(-5)]
        public void Constructor_WithACoalesceWindowThatIsNotPositive_ThrowsCoalesceWindowNotPositive(int window)
        {
            ISaveService service = NewSaveService(new FakeSaveStore());

            SaveException error = Assert.Throws<SaveException>(
                () => new SaveScheduler<DummyState>(service, "key", new FakeGameClock(), window));
            StringAssert.Contains("at least 1ms", error.Message);
        }

        [Test]
        public void CanFlushBlocking_OverANonHoppingSaveService_IsTrue()
        {
            ISaveService service = NewSaveService(new FakeSaveStore());
            using SaveScheduler<DummyState> scheduler = new(service, "key", new FakeGameClock());

            Assert.IsTrue(scheduler.CanFlushBlocking);
        }

        [Test]
        public void CanFlushBlocking_OverASaveServiceComposedOverAThreadHoppingStore_IsFalse()
        {
            ISaveService service = NewSaveService(new ThreadHoppingStore(new FakeSaveStore()));
            using SaveScheduler<DummyState> scheduler = new(service, "key", new FakeGameClock());

            Assert.IsFalse(scheduler.CanFlushBlocking);
        }

        [Test]
        public void AfterDispose_MarkDirty_ThrowsSchedulerDisposed()
        {
            ISaveService service = NewSaveService(new FakeSaveStore());
            SaveScheduler<DummyState> scheduler = new(service, "some-key", new FakeGameClock());
            scheduler.Dispose();

            SaveException error = Assert.Throws<SaveException>(() => scheduler.MarkDirty(new DummyState()));
            StringAssert.Contains("some-key", error.Message);
        }

        [Test]
        public void AfterDispose_FlushAsync_ThrowsSchedulerDisposed()
        {
            ISaveService service = NewSaveService(new FakeSaveStore());
            SaveScheduler<DummyState> scheduler = new(service, "some-key", new FakeGameClock());
            scheduler.Dispose();

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Complete(scheduler.FlushAsync()));
            StringAssert.Contains("some-key", error.Message);
        }

        [Test]
        public void AfterDispose_FlushBlocking_ThrowsSchedulerDisposed()
        {
            ISaveService service = NewSaveService(new FakeSaveStore());
            SaveScheduler<DummyState> scheduler = new(service, "some-key", new FakeGameClock());
            scheduler.Dispose();

            SaveException error = Assert.Throws<SaveException>(() => scheduler.FlushBlocking());
            StringAssert.Contains("some-key", error.Message);
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            ISaveService service = NewSaveService(new FakeSaveStore());
            SaveScheduler<DummyState> scheduler = new(service, "key", new FakeGameClock());

            scheduler.Dispose();

            Assert.DoesNotThrow(() => scheduler.Dispose());
        }

        [Test]
        public void Dispose_WithNothingPending_DoesNotWrite()
        {
            FakeSaveStore store = new();
            ISaveService service = NewSaveService(store);
            SaveScheduler<DummyState> scheduler = new(service, "key", new FakeGameClock());

            Assert.DoesNotThrow(() => scheduler.Dispose());

            Assert.IsFalse(SynchronousUniTask.Result(store.ExistsAsync("key", default)));
        }

        [Test]
        public void MarkDirty_SeveralTimesInsideOneWindow_WritesOnceWhenItElapses_CarryingTheLastState()
        {
            FakeSaveStore store = new();
            ISaveService service = NewReadableSaveService(store);
            FakeGameClock clock = new() { DeltaTime = 0.05f };
            using SaveScheduler<DummyState> scheduler = new(service, Key, clock, WindowMilliseconds);

            scheduler.MarkDirty(new DummyState { Value = 1 });
            scheduler.MarkDirty(new DummyState { Value = 2 });
            scheduler.MarkDirty(new DummyState { Value = 3 });

            clock.AdvanceFrames(FramesPerWindow - 1);
            Assert.AreEqual(0, store.WriteCount, "nothing may be written before the window has elapsed");
            Assert.IsTrue(scheduler.HasPendingWrite);

            clock.AdvanceFrame();
            Assert.AreEqual(1, store.WriteCount, "three MarkDirty calls inside one window have to produce exactly one write");
            Assert.AreEqual(3, StoredValue(service), "the one write has to carry the latest state, not the first");
            Assert.IsFalse(scheduler.HasPendingWrite);

            clock.AdvanceFrames(FramesPerWindow * 3);
            Assert.AreEqual(1, store.WriteCount, "with nothing newly dirty, no later window may write again");
        }

        [Test]
        public void MarkDirty_AfterTheWindowAlreadyWrote_OpensANewWindowAndWritesAgain()
        {
            FakeSaveStore store = new();
            ISaveService service = NewReadableSaveService(store);
            FakeGameClock clock = new() { DeltaTime = 0.05f };
            using SaveScheduler<DummyState> scheduler = new(service, Key, clock, WindowMilliseconds);

            scheduler.MarkDirty(new DummyState { Value = 1 });
            clock.AdvanceFrames(FramesPerWindow);
            Assert.AreEqual(1, store.WriteCount, "guard: the first window has to have written");

            scheduler.MarkDirty(new DummyState { Value = 2 });
            clock.AdvanceFrames(FramesPerWindow);

            Assert.AreEqual(2, store.WriteCount, "new dirty state after a write is a new window, not more of the old one");
            Assert.AreEqual(2, StoredValue(service));
        }

        [Test]
        public void FlushAsync_WritesWhatIsPending_WithoutWaitingForTheWindow()
        {
            FakeSaveStore store = new();
            ISaveService service = NewReadableSaveService(store);
            FakeGameClock clock = new() { DeltaTime = 0.05f };
            using SaveScheduler<DummyState> scheduler = new(service, Key, clock, WindowMilliseconds);

            scheduler.MarkDirty(new DummyState { Value = 7 });

            SynchronousUniTask.Complete(scheduler.FlushAsync());

            Assert.AreEqual(1, store.WriteCount, "FlushAsync has to write now rather than wait out the window");
            Assert.AreEqual(7, StoredValue(service));
            Assert.IsFalse(scheduler.HasPendingWrite);

            clock.AdvanceFrames(FramesPerWindow * 3);
            Assert.AreEqual(1, store.WriteCount, "the window FlushAsync pre-empted must not fire a second, redundant write");
        }

        [Test]
        public void FlushAsync_WithNothingPending_WritesNothing()
        {
            FakeSaveStore store = new();
            using SaveScheduler<DummyState> scheduler = new(NewReadableSaveService(store), Key, new FakeGameClock(), WindowMilliseconds);

            SynchronousUniTask.Complete(scheduler.FlushAsync());

            Assert.AreEqual(0, store.WriteCount);
        }

        /// <remarks>
        /// See docs/saving.md, "A failed write now says so".
        /// </remarks>
        [Test]
        public void AWindowWriteThatFails_IsLoggedOnceNamingTheKey_StaysPending_AndTheNextWindowLandsIt()
        {
            FakeSaveStore store = new() { FailNextWrites = 1 };
            ISaveService service = NewReadableSaveService(store);
            FakeGameClock clock = new() { DeltaTime = 0.05f };
            using SaveScheduler<DummyState> scheduler = new(service, Key, clock, WindowMilliseconds);

            scheduler.MarkDirty(new DummyState { Value = 5 });

            ExpectOneFailedWriteLog();
            clock.AdvanceFrames(FramesPerWindow);

            Assert.AreEqual(0, store.WriteCount, "guard: the first attempt has to have failed");
            Assert.IsTrue(scheduler.HasPendingWrite, "a failed write must not be dropped - the state was never saved");
            Assert.IsFalse(scheduler.IsFlushing, "and it must not still be reported as in progress");

            clock.AdvanceFrames(FramesPerWindow);

            Assert.AreEqual(1, store.WriteCount, "the next window has to retry the failed write and land it");
            Assert.AreEqual(5, StoredValue(service));
            Assert.IsFalse(scheduler.HasPendingWrite);
        }

        [Test]
        public void AFlushAsyncWriteThatFails_IsNotReportedAsSuccess_AndTheNextWindowLandsIt()
        {
            FakeSaveStore store = new() { FailNextWrites = 1 };
            ISaveService service = NewReadableSaveService(store);
            FakeGameClock clock = new() { DeltaTime = 0.05f };
            using SaveScheduler<DummyState> scheduler = new(service, Key, clock, WindowMilliseconds);

            scheduler.MarkDirty(new DummyState { Value = 6 });

            ExpectOneFailedWriteLog();
            Assert.Catch<SaveException>(() => SynchronousUniTask.Complete(scheduler.FlushAsync()),
                "FlushAsync promises everything pending is durable when it returns; a write that failed cannot return as if it were");

            Assert.AreEqual(0, store.WriteCount, "guard: the attempt has to have failed");
            Assert.IsTrue(scheduler.HasPendingWrite, "a failed write must not be dropped - the state was never saved");

            clock.AdvanceFrames(FramesPerWindow);

            Assert.AreEqual(1, store.WriteCount, "a failed flush has to leave a window counting down to retry it, not a stranded write");
            Assert.AreEqual(6, StoredValue(service));
            Assert.IsFalse(scheduler.HasPendingWrite);
        }

        /// <remarks>
        /// See docs/saving.md, "One write in flight".
        /// </remarks>
        [Test]
        public void StateMarkedDirtyWhileAWriteIsInFlight_IsWhatLands_WhenThatWriteFails()
        {
            FakeSaveStore store = new() { HoldNextWrite = true };
            ISaveService service = NewReadableSaveService(store);
            FakeGameClock clock = new() { DeltaTime = 0.05f };
            using SaveScheduler<DummyState> scheduler = new(service, Key, clock, WindowMilliseconds);

            scheduler.MarkDirty(new DummyState { Value = 1 });
            UniTask flushing = scheduler.FlushAsync();
            Assert.IsTrue(store.HasHeldWrite, "guard: the write carrying 1 has to be in flight");
            Assert.IsTrue(scheduler.IsFlushing);

            scheduler.MarkDirty(new DummyState { Value = 2 });

            ExpectOneFailedWriteLog();
            store.FailHeldWrite();

            Assert.Catch<SaveException>(() => SynchronousUniTask.Complete(flushing),
                "the flush whose write failed cannot report success");
            Assert.IsTrue(scheduler.HasPendingWrite, "the newer state still has to be waiting to be written");

            clock.AdvanceFrames(FramesPerWindow);

            Assert.AreEqual(1, store.WriteCount, "exactly one write has to land: the newer state, retried once");
            Assert.AreEqual(2, StoredValue(service), "the newer state has to win over the one whose write failed");
            Assert.IsFalse(scheduler.HasPendingWrite);
        }

        /// <remarks>
        /// See docs/saving.md, "FlushBlocking, and why it cannot deadlock".
        /// </remarks>
        [Test]
        public void FlushBlocking_WhenTheWriteFails_ThrowsAndKeepsTheStatePending()
        {
            FakeSaveStore store = new() { FailNextWrites = 1 };
            ISaveService service = NewReadableSaveService(store);
            using SaveScheduler<DummyState> scheduler = new(service, Key, new FakeGameClock(), WindowMilliseconds);

            scheduler.MarkDirty(new DummyState { Value = 8 });

            Assert.Catch<SaveException>(() => scheduler.FlushBlocking(), "a failed write cannot return as if it were durable");
            Assert.AreEqual(0, store.WriteCount, "guard: the attempt has to have failed");
            Assert.IsTrue(scheduler.HasPendingWrite, "a failed FlushBlocking must not drop the state it failed to write");

            scheduler.FlushBlocking();

            Assert.AreEqual(1, store.WriteCount, "a second FlushBlocking has to still have something to write");
            Assert.AreEqual(8, StoredValue(service));
        }

        /// <remarks>
        /// See docs/saving.md, "FlushBlocking, and why it cannot deadlock".
        /// </remarks>
        [Test]
        public void FlushBlocking_WhenTheWriteFails_LeavesAWindowCountingDown_AndTheRetryLandsWithoutAnotherCall()
        {
            FakeSaveStore store = new() { FailNextWrites = 1 };
            ISaveService service = NewReadableSaveService(store);
            FakeGameClock clock = new() { DeltaTime = 0.05f };
            using SaveScheduler<DummyState> scheduler = new(service, Key, clock, WindowMilliseconds);

            scheduler.MarkDirty(new DummyState { Value = 9 });

            Assert.Catch<SaveException>(() => scheduler.FlushBlocking(), "a failed write cannot return as if it were durable");
            Assert.AreEqual(0, store.WriteCount, "guard: the attempt has to have failed");
            Assert.IsTrue(scheduler.HasPendingWrite, "guard: the state has to still be waiting to be written");

            clock.AdvanceFrames(FramesPerWindow - 1);

            Assert.AreEqual(0, store.WriteCount, "the retry has to wait out its window, not fire immediately");

            clock.AdvanceFrame();

            Assert.AreEqual(1, store.WriteCount, "the window opened by the failed FlushBlocking has to retry and land the write on its own");
            Assert.AreEqual(9, StoredValue(service));
            Assert.IsFalse(scheduler.HasPendingWrite);

            clock.AdvanceFrames(FramesPerWindow * 3);

            Assert.AreEqual(1, store.WriteCount, "once landed, nothing is left to write again");
        }
    }
}
