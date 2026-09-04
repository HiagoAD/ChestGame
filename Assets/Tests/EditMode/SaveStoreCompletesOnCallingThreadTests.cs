using Company.ChestGame.Saving;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // Every concrete ISaveStore this assembly ships answers CompletesOnCallingThread with a fixed
    // true, and PlayerPrefsStore alone also carries IMainThreadOnlyStore - see docs/saving.md, "The
    // thread hop". None of this needs a real write: every property here is a constant answer on the
    // type, so proving it costs nothing more than constructing the store. Nothing here ever calls
    // WriteAsync, so PlayerPrefsStore's own construction here never touches a real PlayerPrefs key.
    public class SaveStoreCompletesOnCallingThreadTests
    {
        [Test]
        public void FileStore_CompletesOnCallingThread_IsTrue_AndIsNotMainThreadOnly()
        {
            ISaveStore store = new FileStore("unused");

            Assert.IsTrue(store.CompletesOnCallingThread);
            Assert.IsFalse(store is IMainThreadOnlyStore,
                "FileStore touches no Unity API on the path a key resolves through, so it never needed the marker that keeps ThreadHoppingStore from hopping it");
        }

        [Test]
        public void AtomicFileStore_CompletesOnCallingThread_IsTrue_AndIsNotMainThreadOnly()
        {
            ISaveStore store = new AtomicFileStore("unused");

            Assert.IsTrue(store.CompletesOnCallingThread);
            Assert.IsFalse(store is IMainThreadOnlyStore);
        }

        [Test]
        public void InMemoryStore_CompletesOnCallingThread_IsTrue_AndIsNotMainThreadOnly()
        {
            ISaveStore store = new InMemoryStore();

            Assert.IsTrue(store.CompletesOnCallingThread);
            Assert.IsFalse(store is IMainThreadOnlyStore);
        }

        [Test]
        public void PlayerPrefsStore_CompletesOnCallingThread_IsTrue_AndIsMainThreadOnly()
        {
            ISaveStore store = new PlayerPrefsStore("ChestGameSaveTests_unused_");

            Assert.IsTrue(store.CompletesOnCallingThread);
            Assert.IsTrue(store is IMainThreadOnlyStore,
                "every member is a PlayerPrefs call, so ThreadHoppingStore has to leave this one alone rather than hop it");
        }
    }
}
