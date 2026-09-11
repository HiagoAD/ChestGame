namespace Company.ChestGame.Saving
{
    // Turns each selection enum into the component it names. CreateStore, CreateCodec and
    // CreateProtector each hand back one component, never an assembled save pipeline. A value this
    // build does not recognise, such as one serialized by a newer build, gets the default - File,
    // Json or None - rather than an exception. See docs/saving.md for the reasoning behind this.
    public static class SaveComponentFactory
    {
        // Every InMemory store handed out is this one instance, so what it holds lasts for the life
        // of the process however many times it is requested. Construct InMemoryStore directly for
        // an isolated one.
        private static readonly InMemoryStore SharedInMemoryStore = new();

        // Throws SaveException when inputs is null, even for InMemory, which reads nothing from it.
        public static ISaveStore CreateStore(SaveStorage storage, SaveFactoryInputs inputs)
        {
            if (inputs == null) throw SaveException.NoFactoryInputs();

            return storage switch
            {
                SaveStorage.AtomicFile => new AtomicFileStore(inputs.RootDirectory),
                SaveStorage.PlayerPrefs => new PlayerPrefsStore(inputs.PlayerPrefsKeyPrefix),
                SaveStorage.InMemory => SharedInMemoryStore,
                _ => new FileStore(inputs.RootDirectory)
            };
        }

        public static ISaveCodec CreateCodec(SaveCodec codec) =>
            codec switch
            {
                SaveCodec.Json => new JsonCodec(),
                SaveCodec.JsonPretty => new PrettyJsonCodec(),
                SaveCodec.JsonGzip => new GzipJsonCodec(),
                _ => new JsonCodec()
            };

        // Throws SaveException when inputs is null, even for None and Base64, which need no key.
        public static IPayloadProtector CreateProtector(SaveProtection protection, SaveFactoryInputs inputs)
        {
            if (inputs == null) throw SaveException.NoFactoryInputs();

            return protection switch
            {
                SaveProtection.None => new NoProtection(),
                SaveProtection.Base64 => new Base64Obfuscator(),
                SaveProtection.Xor => new XorObfuscator(inputs.XorKey),
                SaveProtection.Hmac => new HmacSignedProtector(inputs.HmacKey),
                SaveProtection.Aes => new AesProtector(inputs.AesKey),
                _ => new NoProtection()
            };
        }
    }
}
