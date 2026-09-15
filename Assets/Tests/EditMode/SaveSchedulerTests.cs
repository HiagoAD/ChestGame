using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers the parts of <see cref="SaveScheduler{T}"/> provable without a real thread hop or a
    /// real clock: its own constructor guards, <c>CanFlushBlocking</c> answering from the
    /// <see cref="ISaveService"/> it was given, and the exceptions thrown once <c>Dispose</c> has
    /// run.
    /// </summary>
    /// <remarks>
    /// See docs/testing.md, "The save fixtures".
    /// </remarks>
    public class SaveSchedulerTests
    {
        private class DummyState
        {
            public int Value;
        }

        private static ISaveService NewSaveService(ISaveStore store) =>
            new SaveService(new FakeSaveCodec(), new NoProtection(), store);

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
    }
}
