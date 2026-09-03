using System;
using System.Text;
using Company.ChestGame.Saving;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    // Base64Obfuscator takes no constructor argument - unlike Xor, Hmac and Aes, there is no key
    // for it to reject, so there is no NoProtectorKey case to test here. See docs/saving.md,
    // "Base64Obfuscator".
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
