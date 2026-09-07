using System.Text;

namespace Company.ChestGame.Saving
{
    // Everything SaveComponentFactory needs that the three selection enums cannot carry: where a
    // file-backed store keeps its files, what namespaces a PlayerPrefs key, and the key material
    // Xor, Hmac and Aes each need before they can run at all. A plain instance, not a static holder
    // - the split this type exists for is documented on SaveServiceFactory and SaveComponentFactory;
    // this is only the data both of them pass around.
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

        // Exactly today's values, so a caller that does not care about any of this keeps getting
        // what it always got. rootDirectory and playerPrefsKeyPrefix are the two things a test
        // already had reason to redirect - away from the developer's real save directory or real
        // editor prefs - so both stay overridable here the same way Create/CreateFrom's own
        // optional parameters let a caller redirect one without inventing values for the rest.
        //
        // Each default key below is its own name, UTF8-encoded - fine for a showcase with no key
        // management story to demonstrate, but say so plainly rather than let three constants that
        // happen to compile look like considered key material: nothing about
        // "Company.ChestGame.Saving.DefaultXorKey" as bytes resists anyone who opens this repository
        // or decompiles the build. A real game would instead bake in a key generated for that build
        // rather than typed into source control, and - for whichever of these three protectors is
        // actually meant to resist something rather than merely deter a curious player - issue or
        // derive it per install rather than share one key across every copy of the binary. Neither
        // of those changes what "the key ships in the binary either way" already concedes; see
        // docs/saving.md, "The protectors, and what a key shipping inside the binary buys".
        public static SaveFactoryInputs Defaults(string rootDirectory = null, string playerPrefsKeyPrefix = null) =>
            new(
                rootDirectory ?? FileStore.DefaultRootDirectory(),
                playerPrefsKeyPrefix ?? DefaultPlayerPrefsKeyPrefix,
                Encoding.UTF8.GetBytes("Company.ChestGame.Saving.DefaultXorKey"),
                Encoding.UTF8.GetBytes("Company.ChestGame.Saving.DefaultHmacKey"),
                Encoding.UTF8.GetBytes("Company.ChestGame.Saving.DefaultAesKey"));
    }
}
