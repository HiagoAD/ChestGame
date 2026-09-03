using System;
using System.Collections.Generic;

namespace Company.ChestGame.Saving
{
    // Human-readable warnings about a profile's codec/protector combination. Never errors: nothing
    // returned here stops SaveServiceFactory from building whatever the profile actually asks for.
    // Worth having now that SaveCodec and SaveProtection each carry more than one real choice.
    public static class SaveProfileValidator
    {
        public static IReadOnlyList<string> Validate(SaveProfileSO profile)
        {
            // Unity-null, not C#-null, for the reason SaveServiceFactory.Create checks it the same
            // way: a destroyed profile has nothing to warn about, only something for the factory to
            // refuse outright.
            if (profile == null) return Array.Empty<string>();

            return Validate(profile.Codec, profile.Protection);
        }

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

            // Deliberately not warned about: JsonGzip paired with an encrypting protector. The
            // pipeline is state -> ISaveCodec -> IPayloadProtector (SaveService.SaveAsync encodes
            // first and protects what the codec returned), so gzip always compresses the plaintext
            // before anything encrypts it. "Compression after encryption buys nothing" is true, but
            // it describes the opposite order to the one this pipeline runs — encrypting a codec's
            // output can never make that codec's own compression pointless, because the compression
            // already happened first. That failure mode would need IPayloadProtector to run before
            // ISaveCodec, which nothing in this architecture does.

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
