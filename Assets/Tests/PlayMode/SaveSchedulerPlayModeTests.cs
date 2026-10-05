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
    /// <summary>
    /// The parts of <c>SaveScheduler&lt;T&gt;</c> that need a real <c>UnityGameClock</c>, a real
    /// hop, or a real player loop to settle mid-flight state deterministically: coalescing, one
    /// write in flight, <c>FlushBlocking</c>'s throw over a genuinely hopping composition, and
    /// <c>Dispose</c>'s own best-effort flush and its logged loss.
    /// </summary>
    /// <remarks>
    /// See docs/testing.md, "The save fixtures".
    /// </remarks>
    public class SaveSchedulerPlayModeTests
    {
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
            foreach (RecordingSaveStore store in _blockingStores) store.ReleaseWrite();
            foreach (IDisposable scheduler in _schedulers)
            {
                try { scheduler.Dispose(); }
                catch { }
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
            await UniTask.Yield();
            await UniTask.Yield();

            Assert.AreEqual(1, store.WriteCount, "three MarkDirty calls inside one coalescing window must produce a single write");
            RecordingSaveState written = await service.LoadAsync<RecordingSaveState>(key, CancellationToken.None);
            Assert.AreEqual(3, written.Value, "the one write must carry the latest state, not the first");

            scheduler.MarkDirty(new RecordingSaveState { Value = 4 });
            await WaitFor(() => store.WriteCount >= 2);

            Assert.AreEqual(2, store.WriteCount);
        });

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

            Assert.IsFalse(scheduler.HasPendingWrite, "guard: nothing must be queued behind the in-flight write for this test");

            Assert.DoesNotThrow(() => scheduler.Dispose());

            inner.ReleaseWrite();

            await WaitFor(() => inner.WriteCount >= 1);
            await UniTask.Delay(WindowMilliseconds * 3);

            Assert.AreEqual(1, inner.WriteCount, "the one abandoned write must land exactly once and never repeat or retry after Dispose");
        });

        /// <remarks>
        /// See docs/saving.md, "Disposal and a pending write".
        /// </remarks>
        [UnityTest]
        public IEnumerator Dispose_WhileACoalescingWindowIsStillCountingDown_CancelsIt_OverANonHoppingComposition() => UniTask.ToCoroutine(async () =>
        {
            RecordingSaveStore store = TrackedStore();
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), store);
            string key = UniqueKey(nameof(Dispose_WhileACoalescingWindowIsStillCountingDown_CancelsIt_OverANonHoppingComposition));
            SaveScheduler<RecordingSaveState> scheduler = TrackedScheduler(service, key);

            scheduler.MarkDirty(new RecordingSaveState { Value = 1 });
            Assert.IsFalse(scheduler.IsFlushing, "guard: the window must not have elapsed yet - nothing should be flushing");

            Assert.DoesNotThrow(() => scheduler.Dispose());
            Assert.AreEqual(1, store.WriteCount, "guard: Dispose's own best-effort flush has to have written synchronously");

            await UniTask.Delay(WindowMilliseconds * 3);
            await UniTask.Yield();
            await UniTask.Yield();

            Assert.AreEqual(1, store.WriteCount, "Dispose's own best-effort flush must be the only write - the cancelled window must never fire one of its own");
            Assert.IsFalse(scheduler.HasPendingWrite, "nothing may be left pending once Dispose has flushed");
        });

        [UnityTest]
        public IEnumerator Dispose_WithAPendingWriteThatNeverStartedFlushing_OverAHoppingComposition_LogsTheLossAndDoesNotThrow() => UniTask.ToCoroutine(async () =>
        {
            RecordingSaveStore inner = TrackedStore();
            ThreadHoppingStore hopStore = new(inner);
            ISaveService service = new SaveService(new JsonCodec(), new NoProtection(), hopStore);
            string key = UniqueKey(nameof(Dispose_WithAPendingWriteThatNeverStartedFlushing_OverAHoppingComposition_LogsTheLossAndDoesNotThrow));
            SaveScheduler<RecordingSaveState> scheduler = TrackedScheduler(service, key);

            scheduler.MarkDirty(new RecordingSaveState { Value = 1 });
            Assert.IsFalse(scheduler.IsFlushing, "guard: the window must not have elapsed yet - nothing should be flushing");

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(key)));
            Assert.DoesNotThrow(() => scheduler.Dispose());

            await WaitFor(() => inner.WriteCount >= 1);
        });
    }
}
