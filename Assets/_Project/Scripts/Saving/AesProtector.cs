using System;
using System.Security.Cryptography;
using System.Text;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// AES-256-CBC with a random IV per save, encrypt-then-MAC with HMAC-SHA256 over the IV and
    /// ciphertext. Stored layout is IV (16 bytes) || ciphertext || tag (32 bytes).
    /// </summary>
    /// <remarks>
    /// IsTextSafe is false: ciphertext is not JSON.
    /// See docs/saving.md, "The protectors, and what a key shipping inside the binary buys".
    /// </remarks>
    public class AesProtector : IPayloadProtector
    {
        /// <summary>AES block size.</summary>
        private const int IvLength = 16;
        /// <summary>SHA-256 output size.</summary>
        private const int TagLength = 32;

        private readonly byte[] _encryptionKey;
        private readonly byte[] _macKey;

        public string Id => "aes";
        public bool IsTextSafe => false;

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

        /// <summary>
        /// Checks the tag before a single byte reaches AES, so a tampered or wrong-key payload fails
        /// as <see cref="PayloadTamperedException"/> rather than an AES padding or block-alignment
        /// error.
        /// </summary>
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
