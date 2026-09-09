namespace Company.ChestGame.Saving
{
    // Maps each selection enum to the concrete component it names, one entry point per axis:
    // CreateStore, CreateCodec, CreateProtector. Assembling the results into a working save
    // pipeline happens elsewhere; this only ever hands back one component at a time. See
    // docs/saving.md for the reasoning behind that split.
    public static class SaveComponentFactory
    {
        // Shared rather than built fresh per call: the other three backends already let two stores
        // built from identical arguments see each other's writes, and a fresh InMemoryStore per
        // call would be the one backend that does not - silently losing whatever was "saved" to the
        // instance nobody kept a reference to. A test wanting an isolated instance constructs
        // InMemoryStore directly instead of going through here.
        private static readonly InMemoryStore SharedInMemoryStore = new();

        // inputs is guarded here or nowhere: the components built below never see it, only the one
        // field this method pulls out of it. Required even for InMemory, which reads nothing from
        // it, so a caller who later adds a File- or PlayerPrefs-backed profile does not inherit an
        // inputs that only worked by accident.
        //
        // All three switches here keep a working `_ =>` arm rather than a throw. The enum switched
        // on is a serialized field, which can legally hold a member this build has never heard of -
        // an older build's profile, read after a newer build added a member - and refusing to
        // produce a component at all is the worse failure. File, Json and None are each that
        // default, which is also why each sits first in its own enum: index 0 is where a freshly
        // serialized field lands before anyone touches it, so the fallback and the starting value
        // are the same member.
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

        // The Json arm is explicit rather than folded into the discard, so a missing case for a
        // future member is visible in review instead of silently falling back. No SaveFactoryInputs
        // parameter: no codec this assembly ships needs anything beyond the bytes it is handed.
        public static ISaveCodec CreateCodec(SaveCodec codec) =>
            codec switch
            {
                SaveCodec.Json => new JsonCodec(),
                SaveCodec.JsonPretty => new PrettyJsonCodec(),
                SaveCodec.JsonGzip => new GzipJsonCodec(),
                _ => new JsonCodec()
            };

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
