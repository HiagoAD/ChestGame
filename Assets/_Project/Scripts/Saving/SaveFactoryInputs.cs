using System.Text;

namespace Company.ChestGame.Saving
{
    // Everything a store or protector needs beyond the three selection enums: where a file-backed
    // store keeps its files, what namespaces a PlayerPrefs key, and the key material Xor, Hmac and
    // Aes each need. Plain data, nothing more.
    public sealed class SaveFactoryInputs
    {
        private const string DefaultPlayerPrefsKeyPrefix = "save.";

        public string RootDirectory { get; }
        public string PlayerPrefsKeyPrefix { get; }
        public byte[] XorKey { get; }
        public byte[] HmacKey { get; }
        public byte[] AesKey { get; }

        public SaveFactoryInputs(string rootDirectory, string playerPrefsKeyPrefix, byte[] xorKey, byte[] hmacKey, byte[] aesKey)
        {
            RootDirectory = rootDirectory;
            PlayerPrefsKeyPrefix = playerPrefsKeyPrefix;
            XorKey = xorKey;
            HmacKey = hmacKey;
            AesKey = aesKey;
        }

        // rootDirectory and playerPrefsKeyPrefix stay overridable because a test has reason to
        // redirect both away from the developer's real save directory and real editor prefs.
        //
        // Each default key below is its own name, UTF8-encoded. That is not considered key
        // material and must not be read as any: nothing about these bytes resists anyone who opens
        // this repository or decompiles the build. See docs/saving.md for what a key shipping
        // inside the binary buys and does not.
        public static SaveFactoryInputs Defaults(string rootDirectory = null, string playerPrefsKeyPrefix = null) =>
            new(
                rootDirectory ?? FileStore.DefaultRootDirectory(),
                playerPrefsKeyPrefix ?? DefaultPlayerPrefsKeyPrefix,
                Encoding.UTF8.GetBytes("Company.ChestGame.Saving.DefaultXorKey"),
                Encoding.UTF8.GetBytes("Company.ChestGame.Saving.DefaultHmacKey"),
                Encoding.UTF8.GetBytes("Company.ChestGame.Saving.DefaultAesKey"));
    }
}
