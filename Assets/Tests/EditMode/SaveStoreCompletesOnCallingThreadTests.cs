using System;
using System.IO;
using System.Threading;
using Company.ChestGame.Saving;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Drives every concrete <c>ISaveStore</c> this assembly ships through Write, Read, Exists and
    /// Delete, and requires each returned task to be finished the instant the call returns.
    /// <c>PlayerPrefsStore</c> alone also carries <c>IMainThreadOnlyStore</c>, pinned alongside.
    /// </summary>
    /// <remarks>
    /// Uses real disk and real PlayerPrefs, isolated by a GUID temp root and a GUID key prefix that
    /// <c>TearDown</c> removes.
    /// See docs/saving.md, "The thread hop, and why it is not inside SaveService".
    /// See docs/testing.md, "The save suites never touch a real save".
    /// </remarks>
    public class SaveStoreCompletesOnCallingThreadTests
    {
        private const string Key = "save";

        private string _root;
        private string _prefsPrefix;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ChestGameSaveTests_" + Guid.NewGuid());
            _prefsPrefix = "ChestGameSaveTests." + Guid.NewGuid() + ".";
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);

            PlayerPrefs.DeleteKey(_prefsPrefix + Key);
            PlayerPrefs.Save();
        }

        /// <remarks>
        /// See docs/testing.md, "The save fixtures".
        /// </remarks>
        private static void AssertEveryCallFinishesBeforeReturning(ISaveStore store)
        {
            Assert.IsTrue(store.CompletesOnCallingThread, "guard: this store has to claim the answer being checked");

            UniTask write = store.WriteAsync(Key, new byte[] { 1, 2, 3 }, CancellationToken.None);
            Assert.AreNotEqual(UniTaskStatus.Pending, write.Status, $"{store.GetType().Name}.WriteAsync was still pending when it returned");
            write.GetAwaiter().GetResult();

            UniTask<byte[]> read = store.ReadAsync(Key, CancellationToken.None);
            Assert.AreNotEqual(UniTaskStatus.Pending, read.Status, $"{store.GetType().Name}.ReadAsync was still pending when it returned");
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, read.GetAwaiter().GetResult(), "guard: the read has to see the write");

            UniTask<bool> exists = store.ExistsAsync(Key, CancellationToken.None);
            Assert.AreNotEqual(UniTaskStatus.Pending, exists.Status, $"{store.GetType().Name}.ExistsAsync was still pending when it returned");
            Assert.IsTrue(exists.GetAwaiter().GetResult(), "guard: the key has to exist after the write");

            UniTask delete = store.DeleteAsync(Key, CancellationToken.None);
            Assert.AreNotEqual(UniTaskStatus.Pending, delete.Status, $"{store.GetType().Name}.DeleteAsync was still pending when it returned");
            delete.GetAwaiter().GetResult();

            UniTask<byte[]> readAbsent = store.ReadAsync(Key, CancellationToken.None);
            Assert.AreNotEqual(UniTaskStatus.Pending, readAbsent.Status, $"{store.GetType().Name}.ReadAsync of an absent key was still pending when it returned");
            Assert.IsNull(readAbsent.GetAwaiter().GetResult(), "guard: the delete has to have removed the key");
        }

        [Test]
        public void FileStore_FinishesEveryCallBeforeReturning_AndIsNotMainThreadOnly()
        {
            ISaveStore store = new FileStore(_root);

            AssertEveryCallFinishesBeforeReturning(store);
            Assert.IsFalse(store is IMainThreadOnlyStore,
                "FileStore touches no Unity API on the path a key resolves through, so it never needed the marker that keeps ThreadHoppingStore from hopping it");
        }

        [Test]
        public void AtomicFileStore_FinishesEveryCallBeforeReturning_AndIsNotMainThreadOnly()
        {
            ISaveStore store = new AtomicFileStore(_root);

            AssertEveryCallFinishesBeforeReturning(store);
            Assert.IsFalse(store is IMainThreadOnlyStore);
        }

        [Test]
        public void InMemoryStore_FinishesEveryCallBeforeReturning_AndIsNotMainThreadOnly()
        {
            ISaveStore store = new InMemoryStore();

            AssertEveryCallFinishesBeforeReturning(store);
            Assert.IsFalse(store is IMainThreadOnlyStore);
        }

        [Test]
        public void PlayerPrefsStore_FinishesEveryCallBeforeReturning_AndIsMainThreadOnly()
        {
            ISaveStore store = new PlayerPrefsStore(_prefsPrefix);

            AssertEveryCallFinishesBeforeReturning(store);
            Assert.IsTrue(store is IMainThreadOnlyStore,
                "every member is a PlayerPrefs call, so ThreadHoppingStore has to leave this one alone rather than hop it");
        }
    }
}
