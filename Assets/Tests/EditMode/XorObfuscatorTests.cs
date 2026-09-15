using System;
using System.Linq;
using System.Text;
using Company.ChestGame.Saving;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Tests <see cref="XorObfuscator"/> directly.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The protectors, and what a key shipping inside the binary buys".
    /// </remarks>
    public class XorObfuscatorTests
    {
        private static byte[] Key(string seed = "XorObfuscatorTests.key") => Encoding.UTF8.GetBytes(seed);

        [Test]
        public void Unprotect_UndoesProtect_WithTheSameKey()
        {
            XorObfuscator protector = new(Key());
            byte[] plain = Encoding.UTF8.GetBytes("chest contents, a fair bit longer than the key");

            byte[] roundTripped = protector.Unprotect(protector.Protect(plain));

            CollectionAssert.AreEqual(plain, roundTripped);
        }

        [Test]
        public void Protect_IsItsOwnInverse_ApplyingItTwiceReturnsTheOriginal()
        {
            XorObfuscator protector = new(Key());
            byte[] plain = Encoding.UTF8.GetBytes("chest contents");

            byte[] twice = protector.Protect(protector.Protect(plain));

            CollectionAssert.AreEqual(plain, twice);
        }

        [Test]
        public void Unprotect_WithADifferentKey_ReturnsDifferentBytes_RatherThanThrowing()
        {
            XorObfuscator writer = new(Key("keyA"));
            XorObfuscator reader = new(Key("keyB-is-not-keyA"));
            byte[] plain = Encoding.UTF8.GetBytes("chest contents, long enough for the key to repeat");

            byte[] protectedBytes = writer.Protect(plain);
            byte[] wrongKeyResult = null;

            Assert.DoesNotThrow(() => wrongKeyResult = reader.Unprotect(protectedBytes),
                "XorObfuscator has no way to know a key is wrong; it must produce bytes, not throw");
            Assert.IsFalse(plain.SequenceEqual(wrongKeyResult),
                "a different key must not happen to undo the same XOR");
        }

        [Test]
        public void Constructor_WithANullKey_ThrowsNoProtectorKey()
        {
            SaveException error = Assert.Throws<SaveException>(() => new XorObfuscator(null));
            StringAssert.Contains("key material", error.Message);
        }

        [Test]
        public void Constructor_WithAnEmptyKey_ThrowsNoProtectorKey()
        {
            SaveException error = Assert.Throws<SaveException>(() => new XorObfuscator(Array.Empty<byte>()));
            StringAssert.Contains("key material", error.Message);
        }
    }
}
