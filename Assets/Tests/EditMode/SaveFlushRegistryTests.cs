using System.Text.RegularExpressions;
using Company.ChestGame.Saving;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.EditMode
{
    // The seam GameLifetimeScope flushes at pause/quit, and the one place a scheduler defined in an
    // assembly the composition root cannot reference still gets flushed - see docs/saving.md, "Why
    // the flush stopped being one field". Everything here runs from OnApplicationPause or
    // OnApplicationQuit in production, so the properties that matter are the ones about what happens
    // when something goes wrong at the exact moment durability does.
    public class SaveFlushRegistryTests
    {
        private SaveFlushRegistry _registry;

        [SetUp]
        public void SetUp() => _registry = new SaveFlushRegistry();

        [Test]
        public void Register_WhenTheFlushableCannotFlushBlocking_ThrowsNamingItsKey()
        {
            FakeSaveFlushable hopping = new("chests") { CanFlushBlocking = false };

            SaveException error = Assert.Throws<SaveException>(() => _registry.Register(hopping));

            StringAssert.Contains("chests", error.Message,
                "a wiring failure that cannot say which save it refused is most of the way to no failure at all");
            CollectionAssert.IsEmpty(_registry.Registered);
        }

        [Test]
        public void Register_WithTheSameInstanceTwice_RegistersItOnce()
        {
            FakeSaveFlushable flushable = new();

            _registry.Register(flushable);
            _registry.Register(flushable);

            Assert.AreEqual(1, _registry.Registered.Count);

            _registry.FlushAll();

            Assert.AreEqual(1, flushable.FlushCallCount, "a double registration would flush the same save twice per pause");
        }

        [Test]
        public void Unregister_SomethingNeverRegistered_IsANoOp()
        {
            _registry.Register(new FakeSaveFlushable("currency"));

            Assert.DoesNotThrow(() => _registry.Unregister(new FakeSaveFlushable("chests")));
            Assert.AreEqual(1, _registry.Registered.Count);
        }

        [Test]
        public void FlushAll_WhenOneThrows_StillFlushesTheRest_AndLogsNamingTheKeyThatFailed()
        {
            FakeSaveFlushable failing = new("currency") { FlushThrows = true };
            FakeSaveFlushable healthy = new("meta");

            _registry.Register(failing);
            _registry.Register(healthy);

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("The save under 'currency' failed to flush on pause/quit")));

            Assert.DoesNotThrow(() => _registry.FlushAll());

            Assert.AreEqual(1, healthy.FlushCallCount,
                "fault isolation is per item, not merely 'FlushAll does not propagate' - one bad save must not cost every later one its flush");
        }

        [Test]
        public void FlushAll_WhenAFlushUnregistersAnother_FlushesTheSnapshotExactlyOnceEach()
        {
            FakeSaveFlushable second = new("meta");
            FakeSaveFlushable first = new("currency");
            first.OnFlush = () => _registry.Unregister(second);

            _registry.Register(first);
            _registry.Register(second);

            Assert.DoesNotThrow(() => _registry.FlushAll());

            Assert.AreEqual(1, first.FlushCallCount);
            Assert.AreEqual(1, second.FlushCallCount,
                "FlushAll iterates a snapshot, so everything registered when the pause began still gets its flush");
            CollectionAssert.DoesNotContain(_registry.Registered, second);
        }

        [Test]
        public void FlushAll_WhenAFlushUnregistersItself_DoesNotThrow()
        {
            FakeSaveFlushable selfRemoving = new("chests");
            selfRemoving.OnFlush = () => _registry.Unregister(selfRemoving);

            _registry.Register(selfRemoving);

            Assert.DoesNotThrow(() => _registry.FlushAll());
            Assert.AreEqual(1, selfRemoving.FlushCallCount);
            CollectionAssert.IsEmpty(_registry.Registered);
        }
    }
}
