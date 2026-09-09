using Company.ChestGame.Saving;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // SaveService.CompletesOnCallingThread is a pure pass-through to whatever store it was composed
    // with (docs/saving.md, "FlushBlocking, and why it cannot deadlock") rather than a type check
    // against ThreadHoppingStore by name. Proven here against both answers without needing a real
    // hop: ThreadHoppingStore's own CompletesOnCallingThread (proven in ThreadHoppingStoreTests)
    // already answers both ways depending on what it wraps, which is enough to drive this through
    // both branches from the outside.
    public class SaveServiceCompletesOnCallingThreadTests
    {
        [Test]
        public void OverAnOrdinaryStore_IsTrue()
        {
            ISaveService service = new SaveService(new FakeSaveCodec(), new NoProtection(), new FakeSaveStore());

            Assert.IsTrue(service.CompletesOnCallingThread);
        }

        [Test]
        public void OverAThreadHoppingStore_WrappingAnOrdinaryStore_IsFalse()
        {
            ISaveService service = new SaveService(new FakeSaveCodec(), new NoProtection(), new ThreadHoppingStore(new FakeSaveStore()));

            Assert.IsFalse(service.CompletesOnCallingThread);
        }

        [Test]
        public void OverAThreadHoppingStore_WrappingAMainThreadOnlyStore_IsTrue()
        {
            ISaveService service = new SaveService(new FakeSaveCodec(), new NoProtection(), new ThreadHoppingStore(new FakeMainThreadOnlyStore()));

            Assert.IsTrue(service.CompletesOnCallingThread);
        }
    }
}
