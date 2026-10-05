using System;
using System.Text;
using Company.ChestGame.Saving;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Tests <see cref="HmacSignedProtector"/> directly.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The protectors, and what a key shipping inside the binary buys".
    /// See docs/testing.md, "The save fixtures".
    /// </remarks>
    public class HmacSignedProtectorTests
    {
        private static byte[] Key(string seed = "HmacSignedProtectorTests.key") => Encoding.UTF8.GetBytes(seed);

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
            byte[] tooShort = new byte[10];

            Exception error = Assert.Catch(() => protector.Unprotect(tooShort));

            Assert.AreEqual("PayloadTamperedException", error.GetType().Name,
                "a payload too short to carry a signature has to be rejected as tampering, not as an IndexOutOfRangeException or similar");
        }

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
