using System;
using System.Text;

namespace Company.ChestGame.Saving
{
    // Base64-encodes the codec's bytes and nothing more. IsTextSafe is false: the output is ASCII,
    // but it is a base64 string sitting where the envelope expects a JSON value, not JSON itself.
    // That means SaveEnvelope base64-encodes this protector's already-base64 output a second time -
    // see SaveProfileValidator. This is not an oversight to fix: it is the clearest demonstration
    // that base64 buys illegibility, and only illegibility, which the envelope would have produced
    // on its own for any protector that reported IsTextSafe as false.
    public class Base64Obfuscator : IPayloadProtector
    {
        public string Id => "base64";
        public bool IsTextSafe => false;

        public byte[] Protect(byte[] plain) => Encoding.ASCII.GetBytes(Convert.ToBase64String(plain));

        // Lets FormatException propagate: this type has no key to report a failure against.
        public byte[] Unprotect(byte[] stored) => Convert.FromBase64String(Encoding.ASCII.GetString(stored));
    }
}
