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
    // SaveProfileValidator: human-readable warnings only, never errors, over a profile's
    // codec/protector combination. See docs/saving.md, "SaveProfileValidator".
    public class SaveProfileValidatorTests
    {
        private static IEnumerable<SaveProtection> EveryProtectionExceptNone() =>
            Enum.GetValues(typeof(SaveProtection)).Cast<SaveProtection>().Where(p => p != SaveProtection.None);

        // --- JsonPretty warns for any non-None protection -----------------------------------------

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

        // --- Deliberately not flagged: JsonGzip with an encrypting protector (property 10) -------

        [Test]
        public void Validate_JsonGzipWithAes_ReturnsNoWarningAtAll()
        {
            // SaveService.SaveAsync encodes before it protects, so GzipJsonCodec always compresses
            // plain JSON before AesProtector ever sees the bytes - the effective order, not the
            // wasteful "encrypt then compress" one. A warning here would tell a profile author the
            // opposite of what actually happens; this pins the absence so a future "fix" cannot
            // reintroduce it silently. See docs/saving.md, "Deliberately not flagged".
            IReadOnlyList<string> warnings = SaveProfileValidator.Validate(SaveCodec.JsonGzip, SaveProtection.Aes);

            Assert.IsEmpty(warnings);
        }

        // --- Base64, Hmac and Xor are each flagged regardless of codec ----------------------------

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

            // The implementation's own wording ("proves a save was not modified" / "does not hide
            // it"), not the abstract vocabulary ("integrity" / "confidentiality") the property uses
            // to describe it - asserting the literal words would be testing a paraphrase this
            // message never promised to use.
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

        // --- The baseline: no warnings at all ------------------------------------------------------

        [Test]
        public void Validate_JsonWithNone_ReturnsNoWarnings()
        {
            IReadOnlyList<string> warnings = SaveProfileValidator.Validate(SaveCodec.Json, SaveProtection.None);

            Assert.IsEmpty(warnings);
        }

        // --- The SaveProfileSO overload reads the same fields the inspector writes ---------------

        [Test]
        public void Validate_WithANullProfile_ReturnsNoWarnings()
        {
            IReadOnlyList<string> warnings = SaveProfileValidator.Validate((SaveProfileSO)null);

            Assert.IsEmpty(warnings);
        }

        [Test]
        public void Validate_WithADestroyedProfile_ReturnsNoWarnings()
        {
            // Unity-null, not C#-null: a destroyed ScriptableObject still compiles as a non-null
            // reference, the same distinction SaveServiceFactory.Create's own null check exists for.
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
