namespace Company.ChestGame.Saving
{
    // Repeating-key XOR — its own inverse, which is why one method drives both directions.
    // IsTextSafe is false: the output is arbitrary bytes, not JSON. This hides a save from a casual
    // look at the file and nothing more — JSON's own repeated field names give a known-plaintext
    // attack against a repeating key an easy foothold, so treat this as obfuscation, never as
    // encryption. AesProtector is the type that actually claims confidentiality.
    public class XorObfuscator : IPayloadProtector
    {
        private readonly byte[] _key;

        public string Id => "xor";
        public bool IsTextSafe => false;

        public XorObfuscator(byte[] key)
        {
            if (key == null || key.Length == 0) throw SaveException.NoProtectorKey(Id);

            _key = key;
        }

        public byte[] Protect(byte[] plain) => Apply(plain);

        public byte[] Unprotect(byte[] stored) => Apply(stored);

        private byte[] Apply(byte[] bytes)
        {
            byte[] result = new byte[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
            {
                result[i] = (byte)(bytes[i] ^ _key[i % _key.Length]);
            }

            return result;
        }
    }
}
