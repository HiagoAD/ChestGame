using Company.ChestGame.Saving;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="ISaveService.CompletesOnCallingThread"/> as a pure pass-through to whichever
    /// <see cref="ISaveStore"/> the service was composed with.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The thread hop, and why it is not inside SaveService", and
    /// docs/testing.md, "The save fixtures".
    /// </remarks>
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
