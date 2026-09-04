using System.Text;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // SaveService's three-way version branch once a SaveMigrator exists to feed - see
    // docs/saving.md, "Wiring: what LoadAsync does with a migrator". SaveServiceTests already pins
    // the two branches that predate phase 4 (equal, and older-with-no-migrator) and stays
    // untouched by this phase; this file adds the branch phase 4 makes reachable and re-confirms,
    // with a migrator now wired in, that the other two still behave exactly as before.
    public class SaveServiceMigrationTests
    {
        private const string Key = "profile";

        private FakeSaveStore _store;
        private FakeSaveCodec _codec;
        private FakePayloadProtector _protector;

        private class TestState
        {
            public int Value;
        }

        [SetUp]
        public void SetUp()
        {
            _store = new FakeSaveStore();
            _codec = new FakeSaveCodec { Id = "json" };
            _protector = new FakePayloadProtector { Id = "none" };
        }

        private static byte[] Bytes(string text) => new UTF8Encoding(false).GetBytes(text);

        private static string EnvelopeJson(string version) =>
            $@"{{""v"":{version},""codec"":""json"",""prot"":""none"",""enc"":""raw"",""body"":{{}}}}";

        [Test]
        public void LoadAsync_WhenVersionEqualsCurrent_ReadsThroughDecodeUnchanged_EvenWithAMigratorConfigured()
        {
            SaveMigrator migrator = new(new ISaveMigration[] { new FakeSaveMigration(SaveService.CurrentSchemaVersion - 1) });
            SaveService service = new(_codec, _protector, _store, migrator);
            _store.Seed(Key, Bytes(EnvelopeJson(SaveService.CurrentSchemaVersion.ToString())));
            _codec.DecodeResult = _ => new TestState { Value = 7 };

            TestState result = SynchronousUniTask.Result(service.LoadAsync<TestState>(Key, CancellationToken.None));

            Assert.AreEqual(7, result.Value);
            Assert.IsTrue(_codec.DecodeWasCalled, "an equal-version save must still go through Decode<T>, exactly as before phase 4");
            Assert.IsFalse(_codec.ToJsonWasCalled, "the migration path must never run when the stored version already matches");
        }

        [Test]
        public void LoadAsync_WhenVersionIsBelowCurrent_AndNoMigratorIsConfigured_StillThrowsNoMigrationPath()
        {
            // The exact pre-phase-4 behaviour: SaveServiceTests already pins this without touching
            // any phase 4 surface. Repeated here, explicitly alongside a migrator constructed but
            // not supplied to this particular service, to make the "no migrator" half of the
            // branch's own docs paragraph explicit in the same file as its sibling below.
            SaveService service = new(_codec, _protector, _store);
            _store.Seed(Key, Bytes(EnvelopeJson((SaveService.CurrentSchemaVersion - 1).ToString())));

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(service.LoadAsync<TestState>(Key, CancellationToken.None)));

            StringAssert.Contains("no migration chain", error.Message);
            Assert.IsFalse(_codec.ToJsonWasCalled);
            Assert.IsFalse(_codec.DecodeWasCalled);
        }

        [Test]
        public void LoadAsync_WhenVersionIsBelowCurrent_AndAMigratorIsConfigured_WalksTheChainAndMaterialisesTheMigratedDocument()
        {
            int storedVersion = SaveService.CurrentSchemaVersion - 1;
            FakeSaveMigration migration = new(storedVersion, doc =>
            {
                doc["Value"] = 99;
                return doc;
            });
            SaveMigrator migrator = new(new ISaveMigration[] { migration });
            SaveService service = new(_codec, _protector, _store, migrator);
            _store.Seed(Key, Bytes(EnvelopeJson(storedVersion.ToString())));
            _codec.ToJsonResult = @"{""Value"":1}";

            TestState result = SynchronousUniTask.Result(service.LoadAsync<TestState>(Key, CancellationToken.None));

            Assert.AreEqual(99, result.Value, "the materialised value has to come from the migrated document, not the pre-migration one");
            Assert.IsTrue(_codec.ToJsonWasCalled, "the migrated route reaches the codec's own JSON, not Decode<T>");
            Assert.IsFalse(_codec.DecodeWasCalled, "an older-than-current save with a migrator must never reach Decode<T> directly");
            Assert.IsTrue(migration.ApplyWasCalled);
        }

        [Test]
        public void LoadAsync_WhenVersionIsAboveCurrent_StillThrowsVersionTooNew_EvenWithAMigratorConfigured()
        {
            SaveMigrator migrator = new(new ISaveMigration[] { new FakeSaveMigration(SaveService.CurrentSchemaVersion) });
            SaveService service = new(_codec, _protector, _store, migrator);
            _store.Seed(Key, Bytes(EnvelopeJson((SaveService.CurrentSchemaVersion + 1).ToString())));

            SaveException error = Assert.Throws<SaveException>(
                () => SynchronousUniTask.Result(service.LoadAsync<TestState>(Key, CancellationToken.None)));

            StringAssert.Contains("newer than", error.Message);
            Assert.IsFalse(_codec.ToJsonWasCalled, "a newer save is refused outright, never reaching the migrator");
            Assert.IsFalse(_codec.DecodeWasCalled);
        }
    }
}
