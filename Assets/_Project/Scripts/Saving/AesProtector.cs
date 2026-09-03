using System;
using System.Security.Cryptography;
using System.Text;

namespace Company.ChestGame.Saving
{
    // AES-256-CBC with a random IV per save, encrypt-then-MAC with HMAC-SHA256 over the IV and
    // ciphertext. Stored layout is IV (16 bytes) || ciphertext || tag (32 bytes). Unprotect checks
    // the tag before it ever calls into AES, so a tampered or wrong-key body fails as
    // PayloadTamperedException instead of as a padding or block-alignment error out of the AES
    // transform itself.
    //
    // Not AesGcm: this project ships apiCompatibilityLevel 6 (.NET Standard 2.1) with IL2CPP on
    // Android, and AesGcm is documented to throw PlatformNotSupportedException there because the
    // native crypto library IL2CPP links does not carry AEAD support on every platform this game
    // targets. CBC-then-HMAC needs two primitives instead of one, but both are the plain
    // System.Security.Cryptography surface that has shipped since long before .NET Standard 2.1.
    //
    // IsTextSafe is false: ciphertext is not JSON. The key material ships inside the binary either
    // way — see docs/saving.md for what that does and does not buy.
    public class AesProtector : IPayloadProtector
    {
        private const int IvLength = 16; // AES block size.
        private const int TagLength = 32; // SHA-256 output size.

        private readonly byte[] _encryptionKey;
        private readonly byte[] _macKey;

        public string Id => "aes";
        public bool IsTextSafe => false;

        // One key in, two keys out: encryption and authentication each get their own subkey
        // derived from it, rather than one secret doing both jobs. Reusing a single key across two
        // different primitives is a well-known way to weaken both; the derivation costs nothing and
        // avoids it.
        public AesProtector(byte[] key)
        {
            if (key == null || key.Length == 0) throw SaveException.NoProtectorKey(Id);

            _encryptionKey = DeriveSubkey(key, "Company.ChestGame.Saving.AesProtector.encrypt");
            _macKey = DeriveSubkey(key, "Company.ChestGame.Saving.AesProtector.authenticate");
        }

        public byte[] Protect(byte[] plain)
        {
            using Aes aes = Aes.Create();
            aes.Key = _encryptionKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV();

            byte[] iv = aes.IV;
            byte[] ciphertext;
            using (ICryptoTransform encryptor = aes.CreateEncryptor())
            {
                ciphertext = encryptor.TransformFinalBlock(plain, 0, plain.Length);
            }

            byte[] tag = Tag(iv, ciphertext);

            byte[] result = new byte[IvLength + ciphertext.Length + TagLength];
            Buffer.BlockCopy(iv, 0, result, 0, IvLength);
            Buffer.BlockCopy(ciphertext, 0, result, IvLength, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, result, IvLength + ciphertext.Length, TagLength);

            return result;
        }

        // The tag is checked before a single byte reaches AES: decrypting first and finding out
        // afterwards that the bytes were never valid would mean this class ran attacker-influenced
        // bytes through a block cipher before it had any reason to trust them.
        public byte[] Unprotect(byte[] stored)
        {
            if (stored.Length < IvLength + TagLength)
            {
                throw new PayloadTamperedException("The encrypted payload is shorter than an IV plus a tag, so it cannot carry either");
            }

            byte[] iv = new byte[IvLength];
            Buffer.BlockCopy(stored, 0, iv, 0, IvLength);

            int ciphertextLength = stored.Length - IvLength - TagLength;
            byte[] ciphertext = new byte[ciphertextLength];
            Buffer.BlockCopy(stored, IvLength, ciphertext, 0, ciphertextLength);

            byte[] tag = new byte[TagLength];
            Buffer.BlockCopy(stored, IvLength + ciphertextLength, tag, 0, TagLength);

            if (!ConstantTimeCompare.AreEqual(tag, Tag(iv, ciphertext)))
            {
                throw new PayloadTamperedException("The encrypted payload's tag does not match its IV and ciphertext");
            }

            using Aes aes = Aes.Create();
            aes.Key = _encryptionKey;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.IV = iv;

            using ICryptoTransform decryptor = aes.CreateDecryptor();
            return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
        }

        private byte[] Tag(byte[] iv, byte[] ciphertext)
        {
            using HMACSHA256 hmac = new(_macKey);
            hmac.TransformBlock(iv, 0, iv.Length, null, 0);
            hmac.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

            return hmac.Hash;
        }

        private static byte[] DeriveSubkey(byte[] key, string context)
        {
            using HMACSHA256 hmac = new(key);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(context));
        }
    }
}
