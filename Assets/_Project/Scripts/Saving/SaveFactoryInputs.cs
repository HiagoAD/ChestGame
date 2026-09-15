using System.Text;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Everything a store or protector needs beyond the three selection enums: where a file-backed
    /// store keeps its files, what namespaces a PlayerPrefs key, and the key material Xor, Hmac and
    /// Aes each need. Plain data, nothing more.
    /// </summary>
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

        /// <remarks>
        /// See docs/saving.md, "SaveComponentFactory, SaveFactoryInputs and SaveServiceFactory".
        /// See docs/saving.md, "The protectors, and what a key shipping inside the binary buys".
        /// </remarks>
        public static SaveFactoryInputs Defaults(string rootDirectory = null, string playerPrefsKeyPrefix = null) =>
            new(
                rootDirectory ?? FileStore.DefaultRootDirectory(),
                playerPrefsKeyPrefix ?? DefaultPlayerPrefsKeyPrefix,
                Encoding.UTF8.GetBytes("Company.ChestGame.Saving.DefaultXorKey"),
                Encoding.UTF8.GetBytes("Company.ChestGame.Saving.DefaultHmacKey"),
                Encoding.UTF8.GetBytes("Company.ChestGame.Saving.DefaultAesKey"));
    }
}
