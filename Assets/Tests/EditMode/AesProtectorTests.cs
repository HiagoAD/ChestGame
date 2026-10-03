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
        public void Protect_DoesNotCarryAnyBlockOfThePlaintextInTheClear()
        {
            // Every other test here would pass for a protector that wrote IV || plaintext || tag:
            // the round trip works, the IV still differs per save, and the tag still catches an
            // edit. Encryption is the one thing it would not do, so it is checked directly - no
            // block-sized run of a distinctive plaintext may appear anywhere in the output. Real
            // ciphertext matching 16 chosen bytes by chance is not a failure mode worth guarding.
            AesProtector protector = new(Key());
            byte[] plain = Encoding.UTF8.GetBytes("{\"Balance\":987654321,\"Nickname\":\"plaintext-that-must-not-survive\"}");
            Assert.GreaterOrEqual(plain.Length, 32, "guard: the plaintext has to span several blocks");

            byte[] protectedBytes = protector.Protect(plain);

            const int window = 16;
            for (int start = 0; start + window <= plain.Length; start++)
            {
                Assert.AreEqual(-1, IndexOf(protectedBytes, plain, start, window),
                    $"plaintext bytes {start}..{start + window - 1} appear verbatim in the protected output, so it was not encrypted");
            }
        }

        // The tag is the last 32 bytes, and it is the only region SaveServiceTamperDetectionTests
        // ever flips. A MAC computed over the ciphertext alone would pass that and still let the IV
        // be edited - which under CBC rewrites the first plaintext block at will - so the IV and the
        // ciphertext are each tampered with here directly.
        [TestCase(0, TestName = "Unprotect_WithTheFirstIvByteFlipped_IsRejectedAsTampering")]
        [TestCase(16, TestName = "Unprotect_WithTheFirstCiphertextByteFlipped_IsRejectedAsTampering")]
        public void Unprotect_WithOneByteFlippedOutsideTheTag_IsRejectedAsTampering(int index)
        {
            AesProtector protector = new(Key());
            byte[] plain = Encoding.UTF8.GetBytes("{\"Balance\":987654321,\"Nickname\":\"Ada\"}");

            byte[] tampered = protector.Protect(plain);
            Assert.Less(index, tampered.Length - 32, "guard: the byte flipped has to sit before the 32-byte tag");
            tampered[index] ^= 0x01;

            Exception error = Assert.Catch(() => protector.Unprotect(tampered),
                $"a payload edited at byte {index} has to be refused, not decrypted into something that merely looks plausible");
            Assert.AreEqual("PayloadTamperedException", error.GetType().Name,
                $"an edit at byte {index} has to fail the tag check, not surface as a padding or block error out of AES itself");
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

        // Where needle[start..start+length) first occurs in haystack, or -1.
        private static int IndexOf(byte[] haystack, byte[] needle, int start, int length)
        {
            for (int i = 0; i + length <= haystack.Length; i++)
            {
                int matched = 0;
                while (matched < length && haystack[i + matched] == needle[start + matched]) matched++;

                if (matched == length) return i;
            }

            return -1;
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
