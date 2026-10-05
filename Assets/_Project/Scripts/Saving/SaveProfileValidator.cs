using System;
using System.Collections.Generic;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Human-readable warnings about a profile's codec/protector combination. Never errors: nothing
    /// returned here prevents a service being built from the profile it warns about.
    /// </summary>
    public static class SaveProfileValidator
    {
        /// <summary>
        /// Unity-null, not C#-null: a destroyed profile has nothing to warn about, and refusing it
        /// outright is a different method's job.
        /// </summary>
        public static IReadOnlyList<string> Validate(SaveProfileSO profile)
        {
            if (profile == null) return Array.Empty<string>();

            return Validate(profile.Codec, profile.Protection);
        }

        /// <remarks>
        /// See docs/saving.md, "SaveProfileValidator".
        /// </remarks>
        public static IReadOnlyList<string> Validate(SaveCodec codec, SaveProtection protection)
        {
            List<string> warnings = new();

            if (codec == SaveCodec.JsonPretty && protection != SaveProtection.None)
            {
                warnings.Add(
                    $"JsonPretty spends bytes indenting the body for a person to read, but " +
                    $"{protection} makes that body unreadable anyway — the indentation is paid " +
                    "for and then thrown away.");
            }

            if (protection == SaveProtection.Base64)
            {
                warnings.Add(
                    "Base64 reports IsTextSafe as false, so the envelope base64-encodes its output " +
                    "a second time on top of the base64 this protector already produced — a second " +
                    "layer of size overhead for no additional protection over leaving Protection on " +
                    "None.");
            }

            if (protection == SaveProtection.Hmac)
            {
                warnings.Add(
                    "Hmac proves a save was not modified, but does not hide it: the body stays " +
                    "fully readable once decoded, only base64-wrapped by the envelope like any " +
                    "other non-text-safe body. Pick Aes as well if the save also needs to be " +
                    "unreadable.");
            }

            if (protection == SaveProtection.Xor)
            {
                warnings.Add(
                    "Xor is a repeating-key XOR over the codec's own plaintext output, and JSON's " +
                    "repeated field names give a known-plaintext attack against a repeating key an " +
                    "easy foothold. Treat it as obfuscation, not encryption.");
            }

            return warnings;
        }
    }
}
