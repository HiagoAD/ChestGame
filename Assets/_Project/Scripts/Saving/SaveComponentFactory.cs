namespace Company.ChestGame.Saving
{
    // The one place that maps each selection enum to the concrete component it names - the single
    // job that genuinely has to be a factory, because nothing about a serialized enum value lets a
    // DI container pick a type for it at runtime the way it can for something registered by
    // interface. One focused entry point per axis, mirroring the split the enums themselves already
    // draw: storage, codec, protector. See docs/saving.md.
    //
    // What this type is not: it does not assemble a SaveService, does not know about a
    // SaveMigrator, an ILegacyImport, a ThreadHoppingStore or a SaveScheduler<T>, and it never will
    // - see SaveServiceFactory's own header for where each of those actually gets decided.
    public static class SaveComponentFactory
    {
        // Static state, against this assembly's own grain - justified here because the other three
        // backends are already process-global for reasons outside this factory's control: a
        // filesystem and PlayerPrefs' table are shared by construction, so two stores built from
        // identical arguments already see each other's writes. An InMemoryStore built fresh on
        // every CreateStore call would be the one backend where identical arguments produce isolated
        // storage instead, and anything that rebuilds it - a scene change, a re-resolve, a second
        // CreateStore call - would silently lose the save. One shared instance makes InMemory behave
        // like the other three rather than like a scratchpad. InMemoryStore itself stays free of
        // statics, so a test wanting an isolated one still constructs it directly instead of going
        // through this factory.
        private static readonly InMemoryStore SharedInMemoryStore = new();

        // inputs is checked here rather than left to surface as a NullReferenceException the first
        // time an arm below dereferences it. PoolFactory.Create's own missing-prefab case is not a
        // counterexample: that guard exists too, it just sits one level deeper, inside the pool
        // constructor Create hands the prefab to (PoolException.NoPrefab()). There is no deeper
        // level here - FileStore, PlayerPrefsStore, XorObfuscator and friends never see inputs
        // itself, only the one field this method already pulled out of it - so the guard has to
        // live here or nowhere. This assembly already guards exactly this shape seven times
        // (NoStore, NoSaveService, NoClock, NoProfile, NoRootDirectory, NoKeyPrefix,
        // NoProtectorKey); NoFactoryInputs is the eighth, not a new pattern. It matters most right
        // where it is least tested: 6b's composition root is this type's first real caller,
        // assembling by hand rather than through SaveServiceFactory's own defaulting - exactly the
        // path a null slips through.
        //
        // inputs.RootDirectory / inputs.PlayerPrefsKeyPrefix are only read by the arms that need
        // them - InMemory needs neither, so a caller building only ever InMemory-backed services
        // could otherwise pass an inputs with those left null; the guard above still requires a
        // non-null inputs regardless, so that a caller who later adds a File- or PlayerPrefs-backed
        // profile does not inherit a working-by-accident inputs from before. Every one of the three
        // switches in this type keeps a working `_ =>` arm rather than a throw, for the same reason
        // PoolFactory.Create's does: the enum switched on is a serialized field, which can legally
        // hold a member this build's switch has never heard of - an older build's profile, read
        // after a newer build added a storage backend, say - and refusing to produce a component at
        // all is a worse failure than falling back to a working default. File, Json and None are
        // each that default, which is also why each sits first in its own enum: index 0 is where a
        // freshly serialized field lands before anyone has touched the dropdown, so the member a
        // missing case falls back to and the member a new field starts on are the same one.
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
        // future member is visible in review instead of silently falling back - see
        // docs/saving.md, "why every switch has a working default arm". No SaveFactoryInputs
        // parameter at all, let alone a null check for one: no codec this assembly ships needs
        // anything beyond the bytes it is handed.
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
