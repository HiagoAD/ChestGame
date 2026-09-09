using System;
using System.Text;
using Company.ChestGame.Saving;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // HmacSignedProtector directly. See docs/saving.md, "The protectors, and what a key shipping
    // inside the binary buys". PayloadTamperedException is internal and this test assembly has no
    // InternalsVisibleTo into Company.ChestGame.Saving (confirmed absent project-wide), so a
    // failure's exact type is checked by name through reflection rather than by catching the type
    // directly. SaveServiceTamperDetectionTests proves the same class of failure through the public
    // SaveException.PayloadTampered instead.
    public class HmacSignedProtectorTests
    {
        private static byte[] Key(string seed = "HmacSignedProtectorTests.key") => Encoding.UTF8.GetBytes(seed);

        // --- Property 6: HmacSignedProtector specifics --------------------------------------------

        [Test]
        public void Protect_MakesTheOutputExactlyThirtyTwoBytesLongerThanTheInput()
        {
            HmacSignedProtector protector = new(Key());
            byte[] plain = Encoding.UTF8.GetBytes("chest contents");

            byte[] protectedBytes = protector.Protect(plain);

            Assert.AreEqual(plain.Length + 32, protectedBytes.Length,
                "the output is the SHA-256 signature (32 bytes) prepended to the input, nothing more");
        }

        [Test]
        public void Protect_ThenUnprotect_ReturnsTheOriginalPayload()
        {
            HmacSignedProtector protector = new(Key());
            byte[] plain = Encoding.UTF8.GetBytes("chest contents");

            byte[] roundTripped = protector.Unprotect(protector.Protect(plain));

            CollectionAssert.AreEqual(plain, roundTripped);
        }

        [Test]
        public void Unprotect_WithAPayloadShorterThanASignature_IsRejectedAsTamperingRatherThanSomethingUntyped()
        {
            HmacSignedProtector protector = new(Key());
            byte[] tooShort = new byte[10]; // less than the 32-byte signature

            Exception error = Assert.Catch(() => protector.Unprotect(tooShort));

            Assert.AreEqual("PayloadTamperedException", error.GetType().Name,
                "a payload too short to carry a signature has to be rejected as tampering, not as an IndexOutOfRangeException or similar");
        }

        // --- Property 4: a different key reads as tampering ---------------------------------------

        [Test]
        public void Unprotect_WithADifferentKeyThanProtect_IsRejectedAsTampering()
        {
            HmacSignedProtector writer = new(Key("keyA"));
            HmacSignedProtector reader = new(Key("keyB"));
            byte[] plain = Encoding.UTF8.GetBytes("chest contents");

            byte[] protectedBytes = writer.Protect(plain);
            Exception error = Assert.Catch(() => reader.Unprotect(protectedBytes));

            Assert.AreEqual("PayloadTamperedException", error.GetType().Name,
                "a signature computed under a different key must fail exactly like a genuinely tampered one, not surface as garbage returned as if valid");
        }

        // --- Constructor guards (SaveException.NoProtectorKey) ------------------------------------

        [Test]
        public void Constructor_WithANullKey_ThrowsNoProtectorKey()
        {
            SaveException error = Assert.Throws<SaveException>(() => new HmacSignedProtector(null));
            StringAssert.Contains("key material", error.Message);
        }

        [Test]
        public void Constructor_WithAnEmptyKey_ThrowsNoProtectorKey()
        {
            SaveException error = Assert.Throws<SaveException>(() => new HmacSignedProtector(Array.Empty<byte>()));
            StringAssert.Contains("key material", error.Message);
        }
    }
}
