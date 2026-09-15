using System;
using System.Text;
using Company.ChestGame.Saving;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Tests <see cref="AesProtector"/> directly.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The protectors, and what a key shipping inside the binary buys".
    /// See docs/testing.md, "The save fixtures".
    /// </remarks>
    public class AesProtectorTests
    {
        private static byte[] Key(string seed = "AesProtectorTests.key") => Encoding.UTF8.GetBytes(seed);

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
            byte[] tooShort = new byte[10];

            Exception error = Assert.Catch(() => protector.Unprotect(tooShort));

            Assert.AreEqual("PayloadTamperedException", error.GetType().Name,
                "a payload too short to carry an IV and a tag has to be rejected as tampering, not as an IndexOutOfRangeException or similar");
        }

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
