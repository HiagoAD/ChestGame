using System;
using Company.ChestGame.Common;
using Company.ChestGame.Saving;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="SaveMigrator"/> in isolation, against <see cref="FakeSaveMigration"/> steps
    /// rather than a real save model.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "`ISaveMigration` and `SaveMigrator`".
    /// </remarks>
    public class SaveMigratorTests
    {
        private const string Key = "profile";

        [Test]
        public void Migrate_WalksTwoStepsInOrder_TheSecondStepSeesTheFirstStepsOutput()
        {
            FakeSaveMigration v1ToV2 = new(1, doc => { doc["Step1"] = true; return doc; });
            FakeSaveMigration v2ToV3 = new(2, doc => { doc["Step2"] = true; return doc; });
            SaveMigrator migrator = new(new ISaveMigration[] { v1ToV2, v2ToV3 });
            JObject document = new() { ["Value"] = 1 };

            JObject result = migrator.Migrate(Key, document, fromVersion: 1, toVersion: 3);

            Assert.IsTrue((bool)result["Step1"]);
            Assert.IsTrue((bool)result["Step2"]);
            Assert.IsTrue((bool)v2ToV3.LastInput["Step1"],
                "the second step must see the document the first step already produced, not the original input");
        }

        [Test]
        public void Migrate_WhenTheFirstStepIsMissing_ThrowsNoMigrationPath_NamingTheStoredVersion()
        {
            FakeSaveMigration v2ToV3 = new(2);
            SaveMigrator migrator = new(new ISaveMigration[] { v2ToV3 });

            SaveException error = Assert.Throws<SaveException>(
                () => migrator.Migrate(Key, new JObject(), fromVersion: 1, toVersion: 3));

            StringAssert.Contains("schema version 1", error.Message);
            Assert.IsFalse(v2ToV3.ApplyWasCalled, "the walk never reaches a later step when the first one is missing");
        }

        [Test]
        public void Migrate_WhenAMidChainStepIsMissing_ThrowsNoMigrationPath_NamingWhereTheWalkGotStuck_NotTheStoredVersion()
        {
            FakeSaveMigration v1ToV2 = new(1, doc => doc);
            SaveMigrator migrator = new(new ISaveMigration[] { v1ToV2 });

            SaveException error = Assert.Throws<SaveException>(
                () => migrator.Migrate(Key, new JObject(), fromVersion: 1, toVersion: 3));

            StringAssert.Contains("schema version 2", error.Message);
            StringAssert.DoesNotContain("schema version 1,", error.Message,
                "a mid-chain gap must be named by where the walk actually got stuck, not by the originally stored version");
            Assert.IsTrue(v1ToV2.ApplyWasCalled, "the walk has to actually take the first step before finding the gap after it");
        }

        [Test]
        public void Migrate_ToATargetBelowTheStoredVersion_ThrowsSaveMigrationException()
        {
            SaveMigrator migrator = new(new ISaveMigration[] { new FakeSaveMigration(1) });

            Assert.Throws<SaveMigrationException>(
                () => migrator.Migrate(Key, new JObject(), fromVersion: 3, toVersion: 1));
        }

        [Test]
        public void Constructor_WithTwoMigrationsSharingAFromVersion_ThrowsSaveMigrationException_AtConstruction()
        {
            FakeSaveMigration first = new(1);
            FakeSaveMigration second = new(1);

            Assert.Throws<SaveMigrationException>(
                () => new SaveMigrator(new ISaveMigration[] { first, second }));
        }

        /// <remarks>
        /// See docs/architecture.md, "Exception hierarchy".
        /// </remarks>
        [Test]
        public void SaveMigrationException_IsDeliberatelyNotUnderChestGameException()
        {
            FakeSaveMigration first = new(1);
            FakeSaveMigration second = new(1);

            SaveMigrationException failure = Assert.Throws<SaveMigrationException>(
                () => new SaveMigrator(new ISaveMigration[] { first, second }));

            Assert.IsNotInstanceOf<ChestGameException>(failure,
                "or the shell would report a wiring bug to the player as a content download failure and carry on");
            Assert.IsInstanceOf<InvalidOperationException>(failure);
        }

        /// <summary>
        /// See docs/saving.md, "`ISaveMigration` and `SaveMigrator`".
        /// </summary>
        [Test]
        public void Constructor_WithANullMigrationsCollection_DoesNotThrow_AndBehavesAsAnEmptyChain()
        {
            SaveMigrator migrator = null;
            Assert.DoesNotThrow(() => migrator = new SaveMigrator(null));

            JObject document = new() { ["Value"] = 1 };
            JObject result = migrator.Migrate(Key, document, fromVersion: 1, toVersion: 1);
            Assert.AreSame(document, result, "a fromVersion == toVersion walk over an empty chain takes zero steps");

            Assert.Throws<SaveException>(() => migrator.Migrate(Key, document, fromVersion: 1, toVersion: 2));
        }

        [Test]
        public void Migrate_WithANullDocument_ThrowsSaveMigrationException()
        {
            SaveMigrator migrator = new(new ISaveMigration[] { new FakeSaveMigration(1) });

            Assert.Throws<SaveMigrationException>(
                () => migrator.Migrate(Key, null, fromVersion: 1, toVersion: 2));
        }

        [Test]
        public void Migrate_WhenAStepReturnsNull_ThrowsSaveMigrationException_NamingThatStepsFromVersion_RatherThanNReing()
        {
            FakeSaveMigration v1ToV2 = new(1, _ => null);
            FakeSaveMigration v2ToV3 = new(2);
            SaveMigrator migrator = new(new ISaveMigration[] { v1ToV2, v2ToV3 });

            SaveMigrationException error = Assert.Throws<SaveMigrationException>(
                () => migrator.Migrate(Key, new JObject(), fromVersion: 1, toVersion: 3));

            StringAssert.Contains("FromVersion 1", error.Message);
            Assert.IsFalse(v2ToV3.ApplyWasCalled,
                "a step handing back null must be caught immediately, never reaching the next step's Apply(null)");
        }
    }
}
