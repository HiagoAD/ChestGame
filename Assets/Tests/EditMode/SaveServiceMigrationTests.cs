using System.Text;
using System.Threading;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="SaveService"/>'s three-way version branch once a <see cref="SaveMigrator"/>
    /// exists to feed it.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "Wiring: what LoadAsync does with a migrator".
    /// </remarks>
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

        private static string EnvelopeJson(string version, string body = "{}") =>
            $@"{{""v"":{version},""codec"":""json"",""prot"":""none"",""enc"":""raw"",""body"":{body}}}";

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
            // Every link in the chain depends on the one before it: the stored body reaches
            // ToJson, ToJson's text reaches the migration, and the migration builds on the value it
            // was handed rather than overwriting it. A wrong input at any link changes the result.
            int storedVersion = SaveService.CurrentSchemaVersion - 1;
            int? valueTheMigrationSaw = null;
            FakeSaveMigration migration = new(storedVersion, doc =>
            {
                valueTheMigrationSaw = (int)doc["Value"];
                doc["Value"] = valueTheMigrationSaw.Value + 10;
                return doc;
            });
            SaveMigrator migrator = new(new ISaveMigration[] { migration });
            SaveService service = new(_codec, _protector, _store, migrator);
            _store.Seed(Key, Bytes(EnvelopeJson(storedVersion.ToString(), body: @"{""Value"":5}")));
            _codec.ToJsonFromInput = bytes => new UTF8Encoding(false).GetString(bytes);

            TestState result = SynchronousUniTask.Result(service.LoadAsync<TestState>(Key, CancellationToken.None));

            CollectionAssert.AreEqual(Bytes(@"{""Value"":5}"), _codec.LastToJsonInput,
                "ToJson has to be handed the stored body itself, unwrapped and unprotected, not the envelope or anything canned");
            Assert.AreEqual(5, valueTheMigrationSaw,
                "the migration has to see the stored document, not a fresh or default one");
            Assert.AreEqual(15, result.Value, "the materialised value has to come from the migrated document, not the pre-migration one");
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
