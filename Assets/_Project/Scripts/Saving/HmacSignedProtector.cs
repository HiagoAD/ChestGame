using System;
using System.Security.Cryptography;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Prepends an HMAC-SHA256 of the payload to the payload itself, so <see cref="Unprotect"/> can
    /// prove the bytes it was handed are exactly what this key signed, not a modified stand-in for
    /// them.
    /// </summary>
    /// <remarks>
    /// IsTextSafe is false: the signature is arbitrary bytes, not JSON. Proves integrity only: the
    /// payload itself travels unencrypted underneath the signature, readable by anyone who reaches
    /// it.
    /// </remarks>
    public class HmacSignedProtector : IPayloadProtector
    {
        /// <summary>SHA-256 output size, fixed regardless of key length.</summary>
        private const int SignatureLength = 32;

        private readonly byte[] _key;

        public string Id => "hmac";
        public bool IsTextSafe => false;

        public HmacSignedProtector(byte[] key)
        {
            if (key == null || key.Length == 0) throw SaveException.NoProtectorKey(Id);

            _key = key;
        }

        public byte[] Protect(byte[] plain)
        {
            byte[] signature = Sign(plain);
            byte[] result = new byte[SignatureLength + plain.Length];
            Buffer.BlockCopy(signature, 0, result, 0, SignatureLength);
            Buffer.BlockCopy(plain, 0, result, SignatureLength, plain.Length);

            return result;
        }

        /// <summary>
        /// A too-short payload and a mismatched signature mean the same thing to a protector with no
        /// key of its own to explain the difference: both throw
        /// <see cref="PayloadTamperedException"/>.
        /// </summary>
        public byte[] Unprotect(byte[] stored)
        {
            if (stored.Length < SignatureLength)
            {
                throw new PayloadTamperedException("The signed payload is shorter than a signature, so it cannot carry one");
            }

            byte[] signature = new byte[SignatureLength];
            byte[] payload = new byte[stored.Length - SignatureLength];
            Buffer.BlockCopy(stored, 0, signature, 0, SignatureLength);
            Buffer.BlockCopy(stored, SignatureLength, payload, 0, payload.Length);

            if (!ConstantTimeCompare.AreEqual(signature, Sign(payload)))
            {
                throw new PayloadTamperedException("The signed payload's signature does not match its content");
            }

            return payload;
        }

        private byte[] Sign(byte[] payload)
        {
            using HMACSHA256 hmac = new(_key);
            return hmac.ComputeHash(payload);
        }
    }
}
