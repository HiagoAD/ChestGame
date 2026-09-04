using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Company.ChestGame.Common;
using Company.ChestGame.Saving;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.PlayMode
{
    // The parts of SaveScheduler<T> that need a real UnityGameClock, a real hop, or a real player
    // loop to settle mid-flight state deterministically: coalescing, one write in flight,
    // FlushBlocking's throw over a genuinely hopping composition, and Dispose's own best-effort
    // flush and its logged loss. See docs/saving.md, "Write coalescing" through "Disposal and a
    // pending write". SaveSchedulerTests (edit mode) covers the constructor guards and
    // CanFlushBlocking answers that need neither.
    //
    // Every wait below is driven by UniTask.WaitUntil against a counter or a flag this fixture
    // controls (RecordingSaveStore.WriteCount, SaveScheduler<T>.IsFlushing) - never a fixed sleep -
    // bounded by a generous cancellation timeout only so a genuine hang fails loudly instead of
    // stalling the suite. Nothing here asserts on how long anything took.
    public class SaveSchedulerPlayModeTests
    {
        // Short so the suite stays fast - SaveScheduler<T>.DefaultCoalesceWindowMilliseconds (1000ms)
        // exists for production, not for a test waiting on a real clock.
        private const int WindowMilliseconds = 40;

        private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);

        private UnityGameClock _clock;
        private readonly List<RecordingSaveStore> _blockingStores = new();
        private readonly List<IDisposable> _schedulers = new();

        [SetUp]
        public void SetUp() => _clock = new UnityGameClock();

        [TearDown]
        public void TearDown()
        {
            // Never leave a worker thread parked on a gate nobody will ever release again, and never
            // leave a scheduler's own loop running into the next test.
            foreach (RecordingSaveStore store in _blockingStores) store.ReleaseWrite();
            foreach (IDisposable scheduler in _schedulers)
            {
                try { scheduler.Dispose(); }
                catch { /* already exercised deliberately by some of the tests below */ }
            }
        }

        private static string UniqueKey(string name) => $"{name}_{Guid.NewGuid():N}";

        private RecordingSaveStore TrackedStore()
        {
            RecordingSaveStore store = new();
            _blockingStores.Add(store);
            return store;
        }

        private SaveScheduler<RecordingSaveState> TrackedScheduler(ISaveService service, string key, int windowMilliseconds = WindowMilliseconds)
        {
            SaveScheduler<RecordingSaveState> scheduler = new(service, key, _clock, windowMilliseconds);
            _schedulers.Add(scheduler);
            return scheduler;
        }

        private static UniTask WaitFor(Func<bool> condition) =>
            UniTask.WaitUntil(condition, cancellationToken: new CancellationTokenSource(PollTimeout).Token);

        // --- Coalescing (docs/saving.md, "Write coalescing") ---------------------------------

        [UnityTest]
        public IEnumerator MarkDirty_SeveralCallsInsideOneWindow_ProduceExactlyOneWrite_CarryingTheLastState() => UniTask.ToCoroutine(async () =>
        {
            RecordingSaveStore store = TrackedStore();
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), store);
            string key = UniqueKey(nameof(MarkDirty_SeveralCallsInsideOneWindow_ProduceExactlyOneWrite_CarryingTheLastState));
            SaveScheduler<RecordingSaveState> scheduler = TrackedScheduler(service, key);

            scheduler.MarkDirty(new RecordingSaveState { Value = 1 });
            scheduler.MarkDirty(new RecordingSaveState { Value = 2 });
            scheduler.MarkDirty(new RecordingSaveState { Value = 3 });

            await WaitFor(() => store.WriteCount >= 1);
            // Give a bug that double-writes one more frame to show itself before asserting.
            await UniTask.Yield();
            await UniTask.Yield();

            Assert.AreEqual(1, store.WriteCount, "three MarkDirty calls inside one coalescing window must produce a single write");
            RecordingSaveState written = await service.LoadAsync<RecordingSaveState>(key, CancellationToken.None);
            Assert.AreEqual(3, written.Value, "the one write must carry the latest state, not the first");

            // A MarkDirty after the window already fired is new dirty state, not more of the same
            // window - it must produce its own, later write.
            scheduler.MarkDirty(new RecordingSaveState { Value = 4 });
            await WaitFor(() => store.WriteCount >= 2);

            Assert.AreEqual(2, store.WriteCount);
        });

        // --- One write in flight (docs/saving.md, "One write in flight") ---------------------

        [UnityTest]
        public IEnumerator OneWriteInFlight_MarkDirtyTwiceWhileBlocked_ProducesExactlyTwoWrites_SecondCarryingNewestState() => UniTask.ToCoroutine(async () =>
        {
            RecordingSaveStore store = TrackedStore();
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), store);
            string key = UniqueKey(nameof(OneWriteInFlight_MarkDirtyTwiceWhileBlocked_ProducesExactlyTwoWrites_SecondCarryingNewestState));
            SaveScheduler<RecordingSaveState> scheduler = TrackedScheduler(service, key);

            store.ArmBlockingWrite();
            scheduler.MarkDirty(new RecordingSaveState { Value = 1 });

            await WaitFor(() => scheduler.IsFlushing);
            Assert.AreEqual(0, store.WriteCount, "guard: the first write must still be blocked, not already finished");

            scheduler.MarkDirty(new RecordingSaveState { Value = 2 });
            scheduler.MarkDirty(new RecordingSaveState { Value = 3 });

            store.ReleaseWrite();

            await WaitFor(() => !scheduler.IsFlushing);

            Assert.AreEqual(2, store.WriteCount,
                "one in-flight write plus exactly one coalesced follow-up - not three writes, and not a lost update");
            RecordingSaveState written = await service.LoadAsync<RecordingSaveState>(key, CancellationToken.None);
            Assert.AreEqual(3, written.Value, "the follow-up write has to carry the newest state, not the first one queued behind the in-flight write");
        });

        // --- FlushBlocking (docs/saving.md, "FlushBlocking, and why it cannot deadlock") -----

        [Test]
        public void FlushBlocking_OverANonHoppingComposition_WritesSynchronously()
        {
            RecordingSaveStore store = TrackedStore();
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), store);
            string key = UniqueKey(nameof(FlushBlocking_OverANonHoppingComposition_WritesSynchronously));
            SaveScheduler<RecordingSaveState> scheduler = TrackedScheduler(service, key);

            Assert.IsTrue(scheduler.CanFlushBlocking, "guard: this composition must never leave the calling thread");

            scheduler.MarkDirty(new RecordingSaveState { Value = 5 });
            Assert.DoesNotThrow(() => scheduler.FlushBlocking());

            Assert.AreEqual(1, store.WriteCount);
            Assert.IsFalse(scheduler.HasPendingWrite);
        }

        [UnityTest]
        public IEnumerator FlushBlocking_OverAHoppingCompositionWithAWriteGenuinelyMidHop_ThrowsFlushWouldBlock() => UniTask.ToCoroutine(async () =>
        {
            RecordingSaveStore inner = TrackedStore();
            ThreadHoppingStore hopStore = new(inner);
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), hopStore);
            string key = UniqueKey(nameof(FlushBlocking_OverAHoppingCompositionWithAWriteGenuinelyMidHop_ThrowsFlushWouldBlock));
            SaveScheduler<RecordingSaveState> scheduler = TrackedScheduler(service, key);

            Assert.IsFalse(scheduler.CanFlushBlocking, "guard: this composition genuinely leaves the calling thread");

            inner.ArmBlockingWrite();
            scheduler.MarkDirty(new RecordingSaveState { Value = 9 });

            await WaitFor(() => scheduler.IsFlushing);

            SaveException error = Assert.Throws<SaveException>(() => scheduler.FlushBlocking());
            StringAssert.Contains(key, error.Message);
            StringAssert.Contains("leave the calling thread", error.Message);

            inner.ReleaseWrite();
            await WaitFor(() => !scheduler.IsFlushing);
        });

        // --- Dispose (docs/saving.md, "Disposal and a pending write") ------------------------

        [Test]
        public void Dispose_WithAPendingWriteOverANonHoppingComposition_FlushesIt()
        {
            RecordingSaveStore store = TrackedStore();
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), store);
            string key = UniqueKey(nameof(Dispose_WithAPendingWriteOverANonHoppingComposition_FlushesIt));
            SaveScheduler<RecordingSaveState> scheduler = TrackedScheduler(service, key);

            scheduler.MarkDirty(new RecordingSaveState { Value = 11 });

            Assert.DoesNotThrow(() => scheduler.Dispose());

            Assert.AreEqual(1, store.WriteCount);
            Assert.IsFalse(scheduler.HasPendingWrite);
        }

        [UnityTest]
        public IEnumerator Dispose_WithAWriteMidHopAndANewerOneQueued_LogsNamingTheKey_AndDoesNotThrow() => UniTask.ToCoroutine(async () =>
        {
            RecordingSaveStore inner = TrackedStore();
            ThreadHoppingStore hopStore = new(inner);
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), hopStore);
            string key = UniqueKey(nameof(Dispose_WithAWriteMidHopAndANewerOneQueued_LogsNamingTheKey_AndDoesNotThrow));
            SaveScheduler<RecordingSaveState> scheduler = TrackedScheduler(service, key);

            inner.ArmBlockingWrite();
            scheduler.MarkDirty(new RecordingSaveState { Value = 1 });

            await WaitFor(() => scheduler.IsFlushing);

            scheduler.MarkDirty(new RecordingSaveState { Value = 2 });
            Assert.IsTrue(scheduler.HasPendingWrite, "guard: a newer write has to be queued behind the in-flight one");

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(key)));
            Assert.DoesNotThrow(() => scheduler.Dispose());

            inner.ReleaseWrite();
            // Let the abandoned background write settle instead of leaking a live continuation into
            // whatever test runs next.
            await WaitFor(() => inner.WriteCount >= 1);
        });

        [UnityTest]
        public IEnumerator Dispose_WhileAFlushIsGenuinelyInFlight_StopsCleanly_AndNothingKeepsRunningAfterwards() => UniTask.ToCoroutine(async () =>
        {
            RecordingSaveStore inner = TrackedStore();
            ThreadHoppingStore hopStore = new(inner);
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), hopStore);
            string key = UniqueKey(nameof(Dispose_WhileAFlushIsGenuinelyInFlight_StopsCleanly_AndNothingKeepsRunningAfterwards));
            SaveScheduler<RecordingSaveState> scheduler = TrackedScheduler(service, key);

            inner.ArmBlockingWrite();
            scheduler.MarkDirty(new RecordingSaveState { Value = 1 });

            await WaitFor(() => scheduler.IsFlushing);

            // Nothing queued behind the in-flight write this time - the branch this test exists to
            // distinguish from Dispose_WithAWriteMidHopAndANewerOneQueued above, which logs.
            Assert.IsFalse(scheduler.HasPendingWrite, "guard: nothing must be queued behind the in-flight write for this test");

            Assert.DoesNotThrow(() => scheduler.Dispose());

            inner.ReleaseWrite();

            // The abandoned write is left to finish unobserved rather than lost - it still lands -
            // but nothing about the scheduler reacts to it afterwards: no new window, no retry, no
            // further write.
            await WaitFor(() => inner.WriteCount >= 1);
            await UniTask.Delay(WindowMilliseconds * 3);

            Assert.AreEqual(1, inner.WriteCount, "the one abandoned write must land exactly once and never repeat or retry after Dispose");
        });

        // --- Cancellation (docs/saving.md, "Disposal and a pending write") -------------------

        [Test]
        public void Dispose_WhileACoalescingWindowIsStillCountingDown_CancelsIt_OverANonHoppingComposition()
        {
            // Non-hopping: Dispose's own best-effort flush completes synchronously here (the same
            // path Dispose_WithAPendingWriteOverANonHoppingComposition_FlushesIt above proves on its
            // own), which is what proves the window itself never got the chance to fire - if it had,
            // MarkDirty's own coalescing would have produced this write regardless of Dispose, and
            // this test could not tell the difference. Asserting exactly one write, made by Dispose
            // and not by the window, is what pins InterruptWindow() actually running.
            RecordingSaveStore store = TrackedStore();
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), store);
            string key = UniqueKey(nameof(Dispose_WhileACoalescingWindowIsStillCountingDown_CancelsIt_OverANonHoppingComposition));
            SaveScheduler<RecordingSaveState> scheduler = TrackedScheduler(service, key);

            scheduler.MarkDirty(new RecordingSaveState { Value = 1 });
            Assert.IsFalse(scheduler.IsFlushing, "guard: the window must not have elapsed yet - nothing should be flushing");

            Assert.DoesNotThrow(() => scheduler.Dispose());

            Assert.AreEqual(1, store.WriteCount, "Dispose's own best-effort flush must be the only write - the cancelled window must never fire one of its own");
        }

        [UnityTest]
        public IEnumerator Dispose_WithAPendingWriteThatNeverStartedFlushing_OverAHoppingComposition_LogsTheLossAndDoesNotThrow() => UniTask.ToCoroutine(async () =>
        {
            // The window is still counting down - not yet claimed by a flush - so Dispose's own
            // synchronous attempt is what has to leave the calling thread here, a different branch
            // from Dispose_WithAWriteMidHopAndANewerOneQueued above (which finds a flush already
            // running). Both log, naming the key, and neither throws.
            RecordingSaveStore inner = TrackedStore();
            ThreadHoppingStore hopStore = new(inner);
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), hopStore);
            string key = UniqueKey(nameof(Dispose_WithAPendingWriteThatNeverStartedFlushing_OverAHoppingComposition_LogsTheLossAndDoesNotThrow));
            SaveScheduler<RecordingSaveState> scheduler = TrackedScheduler(service, key);

            scheduler.MarkDirty(new RecordingSaveState { Value = 1 });
            Assert.IsFalse(scheduler.IsFlushing, "guard: the window must not have elapsed yet - nothing should be flushing");

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(key)));
            Assert.DoesNotThrow(() => scheduler.Dispose());

            // The abandoned attempt still lands once released - unobserved by the scheduler, but not
            // hung forever - which is what keeps this fixture from leaking a live continuation.
            await WaitFor(() => inner.WriteCount >= 1);
        });
    }
}
