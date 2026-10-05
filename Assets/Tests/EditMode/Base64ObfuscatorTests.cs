using System;
using System.Text;
using Company.ChestGame.Saving;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Tests <see cref="Base64Obfuscator"/> directly. Unlike <see cref="XorObfuscator"/>,
    /// <see cref="HmacSignedProtector"/> and <see cref="AesProtector"/>, it takes no constructor
    /// key, so there is no <c>NoProtectorKey</c> case to test here.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The protectors, and what a key shipping inside the binary buys".
    /// </remarks>
    public class Base64ObfuscatorTests
    {
        [Test]
        public void Protect_ThenUnprotect_ReturnsTheOriginalBytes()
        {
            Base64Obfuscator protector = new();
            byte[] plain = { 0, 1, 2, 254, 255, 65, 66, 67 };

            byte[] roundTripped = protector.Unprotect(protector.Protect(plain));

            CollectionAssert.AreEqual(plain, roundTripped);
        }

        [Test]
        public void Protect_IsPlainBase64_OfTheInputBytes()
        {
            Base64Obfuscator protector = new();
            byte[] plain = { 1, 2, 3, 4, 5 };

            byte[] protectedBytes = protector.Protect(plain);

            Assert.AreEqual(Convert.ToBase64String(plain), Encoding.ASCII.GetString(protectedBytes));
        }
    }
}
