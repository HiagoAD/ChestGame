using System;
using System.Collections.Generic;
using System.Linq;
using Company.ChestGame.Saving;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="SaveProfileValidator"/>'s warnings, which are human-readable only and never
    /// errors, over a profile's codec/protector combination.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "SaveProfileValidator".
    /// </remarks>
    public class SaveProfileValidatorTests
    {
        private static IEnumerable<SaveProtection> EveryProtectionExceptNone() =>
            Enum.GetValues(typeof(SaveProtection)).Cast<SaveProtection>().Where(p => p != SaveProtection.None);

        [TestCaseSource(nameof(EveryProtectionExceptNone))]
        public void Validate_JsonPrettyWithAnyNonNoneProtection_Warns(SaveProtection protection)
        {
            IReadOnlyList<string> warnings = SaveProfileValidator.Validate(SaveCodec.JsonPretty, protection);

            Assert.IsNotEmpty(warnings,
                $"JsonPretty + {protection} spends bytes indenting a body the protector then makes unreadable anyway");
        }

        [Test]
        public void Validate_JsonPrettyWithNone_DoesNotWarn()
        {
            IReadOnlyList<string> warnings = SaveProfileValidator.Validate(SaveCodec.JsonPretty, SaveProtection.None);

            Assert.IsEmpty(warnings);
        }

        /// <remarks>
        /// See docs/saving.md, "SaveProfileValidator".
        /// </remarks>
        [Test]
        public void Validate_JsonGzipWithAes_ReturnsNoWarningAtAll()
        {
            IReadOnlyList<string> warnings = SaveProfileValidator.Validate(SaveCodec.JsonGzip, SaveProtection.Aes);

            Assert.IsEmpty(warnings);
        }

        [Test]
        public void Validate_Base64Protection_Warns()
        {
            IReadOnlyList<string> warnings = SaveProfileValidator.Validate(SaveCodec.Json, SaveProtection.Base64);

            Assert.IsNotEmpty(warnings);
        }

        [Test]
        public void Validate_HmacProtection_WarnsAboutIntegrityWithoutConfidentiality()
        {
            IReadOnlyList<string> warnings = SaveProfileValidator.Validate(SaveCodec.Json, SaveProtection.Hmac);

            Assert.IsTrue(warnings.Any(w =>
                    w.Contains("not modified", StringComparison.OrdinalIgnoreCase) &&
                    w.Contains("does not hide", StringComparison.OrdinalIgnoreCase)),
                "Hmac proves a save was not modified but does not hide it; the warning has to say so");
        }

        [Test]
        public void Validate_XorProtection_WarnsAboutObfuscationNotEncryption()
        {
            IReadOnlyList<string> warnings = SaveProfileValidator.Validate(SaveCodec.Json, SaveProtection.Xor);

            Assert.IsTrue(warnings.Any(w => w.Contains("obfuscation", StringComparison.OrdinalIgnoreCase)),
                "Xor has to be flagged as obfuscation, never as encryption");
        }

        [Test]
        public void Validate_JsonWithNone_ReturnsNoWarnings()
        {
            IReadOnlyList<string> warnings = SaveProfileValidator.Validate(SaveCodec.Json, SaveProtection.None);

            Assert.IsEmpty(warnings);
        }

        [Test]
        public void Validate_WithANullProfile_ReturnsNoWarnings()
        {
            IReadOnlyList<string> warnings = SaveProfileValidator.Validate((SaveProfileSO)null);

            Assert.IsEmpty(warnings);
        }

        /// <remarks>
        /// See docs/saving.md, "SaveComponentFactory, SaveFactoryInputs and SaveServiceFactory".
        /// </remarks>
        [Test]
        public void Validate_WithADestroyedProfile_ReturnsNoWarnings()
        {
            SaveProfileSO profile = ScriptableObject.CreateInstance<SaveProfileSO>();
            Object.DestroyImmediate(profile);

            IReadOnlyList<string> warnings = SaveProfileValidator.Validate(profile);

            Assert.IsEmpty(warnings);
        }

        [Test]
        public void Validate_WithAProfileAuthoredForJsonPrettyAndAes_AgreesWithTheBareEnumOverload()
        {
            SaveProfileSO profile = ScriptableObject.CreateInstance<SaveProfileSO>();
            try
            {
                SerializedObject serialized = new(profile);
                serialized.FindProperty("_codec").enumValueIndex = (int)SaveCodec.JsonPretty;
                serialized.FindProperty("_protection").enumValueIndex = (int)SaveProtection.Aes;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                IReadOnlyList<string> warnings = SaveProfileValidator.Validate(profile);

                Assert.IsNotEmpty(warnings,
                    "the profile overload has to read the same _codec/_protection fields the inspector writes, not defaults of its own");
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }
    }
}
