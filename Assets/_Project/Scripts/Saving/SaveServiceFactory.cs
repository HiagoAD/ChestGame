using System.Text;

namespace Company.ChestGame.Saving
{
    // The one place that turns a profile - or a bare (storage, codec, protection) triple - into an
    // ISaveService. Static and stateless, like PoolFactory and CatalogBuilder.
    public static class SaveServiceFactory
    {
        private const string DefaultPlayerPrefsKeyPrefix = "save.";

        // Xor, Hmac and Aes each need key material their constructor has no default for; a test
        // wanting its own key constructs the protector directly rather than through this factory,
        // the same way playerPrefsKeyPrefix is the only key-shaped thing this factory's own
        // parameter list exposes. Baked-in constants, not derived from anything player- or
        // build-specific: the key ships in the binary either way - see docs/saving.md.
        private static readonly byte[] DefaultXorKey = Encoding.UTF8.GetBytes("Company.ChestGame.Saving.DefaultXorKey");
        private static readonly byte[] DefaultHmacKey = Encoding.UTF8.GetBytes("Company.ChestGame.Saving.DefaultHmacKey");
        private static readonly byte[] DefaultAesKey = Encoding.UTF8.GetBytes("Company.ChestGame.Saving.DefaultAesKey");

        // Static state, against this assembly's own grain - justified here because the other three
        // backends are already process-global for reasons outside this factory's control: a
        // filesystem and PlayerPrefs' table are shared by construction, so two services built from
        // identical arguments already see each other's writes. An InMemoryStore built fresh on every
        // Create/CreateFrom call would be the one backend where identical arguments produce isolated
        // storage instead, and anything that rebuilds the service - a scene change, a re-resolve, a
        // second Create call - would silently lose the save. One shared instance makes InMemory
        // behave like the other three rather than like a scratchpad. InMemoryStore itself stays free
        // of statics, so a test wanting an isolated one still constructs it directly instead of
        // going through this factory.
        private static readonly InMemoryStore SharedInMemoryStore = new();

        public static ISaveService Create(SaveProfileSO profile, string rootDirectory = null, string playerPrefsKeyPrefix = null)
        {
            // Unity-null, not C#-null: a destroyed SaveProfileSO must fail here too.
            if (profile == null) throw SaveException.NoProfile();

            return CreateFrom(profile.Storage, profile.Codec, profile.Protection, rootDirectory, playerPrefsKeyPrefix);
        }

        public static ISaveService CreateFrom(SaveStorage storage, SaveCodec codec, SaveProtection protection, string rootDirectory = null, string playerPrefsKeyPrefix = null) =>
            new SaveService(CreateCodec(codec), CreateProtector(protection), CreateStore(storage, rootDirectory, playerPrefsKeyPrefix));

        private static ISaveStore CreateStore(SaveStorage storage, string rootDirectory, string playerPrefsKeyPrefix)
        {
            string root = rootDirectory ?? FileStore.DefaultRootDirectory();
            string prefix = playerPrefsKeyPrefix ?? DefaultPlayerPrefsKeyPrefix;

            return storage switch
            {
                SaveStorage.AtomicFile => new AtomicFileStore(root),
                SaveStorage.PlayerPrefs => new PlayerPrefsStore(prefix),
                SaveStorage.InMemory => SharedInMemoryStore,

                // A working store rather than a throw, so a profile left on a member this switch
                // has not heard of still comes up with a working save. File is the baseline for
                // the same reason it sits first in the enum.
                _ => new FileStore(root)
            };
        }

        // The Json arm is explicit rather than folded into the discard, so a missing case for a
        // future member is visible in review instead of silently falling back - see
        // docs/saving.md, "why every switch has a working default arm".
        private static ISaveCodec CreateCodec(SaveCodec codec) =>
            codec switch
            {
                SaveCodec.Json => new JsonCodec(),
                SaveCodec.JsonPretty => new PrettyJsonCodec(),
                SaveCodec.JsonGzip => new GzipJsonCodec(),
                _ => new JsonCodec()
            };

        private static IPayloadProtector CreateProtector(SaveProtection protection) =>
            protection switch
            {
                SaveProtection.None => new NoProtection(),
                SaveProtection.Base64 => new Base64Obfuscator(),
                SaveProtection.Xor => new XorObfuscator(DefaultXorKey),
                SaveProtection.Hmac => new HmacSignedProtector(DefaultHmacKey),
                SaveProtection.Aes => new AesProtector(DefaultAesKey),
                _ => new NoProtection()
            };
    }
}
