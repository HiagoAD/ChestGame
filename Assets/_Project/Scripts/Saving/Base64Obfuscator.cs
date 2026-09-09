using System;
using System.Text;

namespace Company.ChestGame.Saving
{
    // Base64-encodes the codec's bytes and nothing more. Obfuscation only: anyone can reverse it
    // without a key. IsTextSafe is false: the output is ASCII, but a base64 string is not itself a
    // JSON value.
    public class Base64Obfuscator : IPayloadProtector
    {
        public string Id => "base64";
        public bool IsTextSafe => false;

        public byte[] Protect(byte[] plain) => Encoding.ASCII.GetBytes(Convert.ToBase64String(plain));

        // Lets FormatException propagate: this type has no key to report a failure against.
        public byte[] Unprotect(byte[] stored) => Convert.FromBase64String(Encoding.ASCII.GetString(stored));
    }
}
