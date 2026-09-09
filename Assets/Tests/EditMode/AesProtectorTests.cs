using System;
using System.Text;
using Company.ChestGame.Saving;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // AesProtector directly: AES-256-CBC with a random IV per save, encrypt-then-MAC, the tag
    // checked through ConstantTimeCompare before a single byte reaches AES. See docs/saving.md,
    // "The protectors, and what a key shipping inside the binary buys".
    //
    // PayloadTamperedException is internal and this test assembly has no InternalsVisibleTo into
    // Company.ChestGame.Saving (confirmed absent project-wide), so a failure's exact type is
    // checked by name through reflection here rather than by catching the type directly -
    // Exception.GetType() is accessible regardless of the type's own visibility.
    // SaveServiceTamperDetectionTests proves the same class of failure through the public
    // SaveException.PayloadTampered instead, which is what an actual caller ever sees.
    public class AesProtectorTests
    {
        private static byte[] Key(string seed = "AesProtectorTests.key") => Encoding.UTF8.GetBytes(seed);

        // --- Property 5: AesProtector specifics --------------------------------------------------

        [Test]
        public void Protect_TheSamePlaintextTwice_ProducesDifferentBytes_ButBothStillDecryptToIt()
        {
            AesProtector protector = new(Key());
            byte[] plain = Encoding.UTF8.GetBytes("chest contents");

            byte[] first = protector.Protect(plain);
            byte[] second = protector.Protect(plain);

            Assert.IsFalse(BytesEqual(first, second),
                "a random IV per save means encrypting the same plaintext twice must not produce identical bytes");
            CollectionAssert.AreEqual(plain, protector.Unprotect(first));
            CollectionAssert.AreEqual(plain, protector.Unprotect(second));
        }

        [Test]
        public void Unprotect_WithAPayloadShorterThanAnIvPlusATag_IsRejectedAsTamperingRatherThanSomethingUntyped()
        {
            AesProtector protector = new(Key());
            byte[] tooShort = new byte[10]; // less than 16 (IV) + 32 (tag) = 48

            Exception error = Assert.Catch(() => protector.Unprotect(tooShort));

            Assert.AreEqual("PayloadTamperedException", error.GetType().Name,
                "a payload too short to carry an IV and a tag has to be rejected as tampering, not as an IndexOutOfRangeException or similar");
        }

        // --- Property 4: a different key reads as tampering, not as a CryptographicException -----

        [Test]
        public void Unprotect_WithADifferentKeyThanProtect_IsRejectedAsTamperingRatherThanACryptographicException()
        {
            AesProtector writer = new(Key("keyA"));
            AesProtector reader = new(Key("keyB"));
            byte[] plain = Encoding.UTF8.GetBytes("chest contents");

            byte[] protectedBytes = writer.Protect(plain);
            Exception error = Assert.Catch(() => reader.Unprotect(protectedBytes));

            Assert.AreEqual("PayloadTamperedException", error.GetType().Name,
                "encrypt-then-MAC checks the tag before a single byte reaches AES, so a wrong key must fail the tag check rather than surface as a CryptographicException out of the AES transform, or as garbage returned as if valid");
        }

        // --- Constructor guards (SaveException.NoProtectorKey) -----------------------------------

        [Test]
        public void Constructor_WithANullKey_ThrowsNoProtectorKey()
        {
            SaveException error = Assert.Throws<SaveException>(() => new AesProtector(null));
            StringAssert.Contains("key material", error.Message);
        }

        [Test]
        public void Constructor_WithAnEmptyKey_ThrowsNoProtectorKey()
        {
            SaveException error = Assert.Throws<SaveException>(() => new AesProtector(Array.Empty<byte>()));
            StringAssert.Contains("key material", error.Message);
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }

            return true;
        }
    }
}
