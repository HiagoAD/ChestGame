namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Turns each selection enum into the component it names. <see cref="CreateStore"/>,
    /// <see cref="CreateCodec"/> and <see cref="CreateProtector"/> each hand back one component,
    /// never an assembled save pipeline. A value this build does not recognise, such as one
    /// serialized by a newer build, gets the default - File, Json or None - rather than an
    /// exception.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "SaveComponentFactory, SaveFactoryInputs and SaveServiceFactory".
    /// </remarks>
    public static class SaveComponentFactory
    {
        /// <summary>
        /// The single <see cref="InMemoryStore"/> handed out for every <see cref="SaveStorage.InMemory"/>
        /// request, so what it holds lasts for the life of the process however many times it is
        /// requested. Construct <see cref="InMemoryStore"/> directly for an isolated one.
        /// </summary>
        private static readonly InMemoryStore SharedInMemoryStore = new();

        /// <summary>
        /// Builds the <see cref="ISaveStore"/> named by <paramref name="storage"/>.
        /// </summary>
        /// <exception cref="SaveException">
        /// When <paramref name="inputs"/> is null, even for <see cref="SaveStorage.InMemory"/>,
        /// which reads nothing from it.
        /// </exception>
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

        /// <summary>
        /// Builds the <see cref="IPayloadProtector"/> named by <paramref name="protection"/>.
        /// </summary>
        /// <exception cref="SaveException">
        /// When <paramref name="inputs"/> is null, even for <see cref="SaveProtection.None"/> and
        /// <see cref="SaveProtection.Base64"/>, which need no key.
        /// </exception>
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
