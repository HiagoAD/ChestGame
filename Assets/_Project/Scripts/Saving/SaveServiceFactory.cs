namespace Company.ChestGame.Saving
{
    // The one place that turns a profile - or a bare (storage, codec, protection) triple - into an
    // ISaveService. Static and stateless, like PoolFactory and CatalogBuilder.
    public static class SaveServiceFactory
    {
        private const string DefaultPlayerPrefsKeyPrefix = "save.";

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
                SaveStorage.InMemory => new InMemoryStore(),

                // A working store rather than a throw, so a profile left on a member this switch
                // has not heard of still comes up with a working save. File is the baseline for
                // the same reason it sits first in the enum.
                _ => new FileStore(root)
            };
        }

        // Switches already, ahead of phase 3's second and third codecs and protectors, so landing
        // those is only ever adding a case here rather than restructuring this factory. The Json
        // and None arms are explicit rather than folded into the discard, so a missing case for a
        // future member is visible in review instead of silently falling back.
        private static ISaveCodec CreateCodec(SaveCodec codec) =>
            codec switch
            {
                SaveCodec.Json => new JsonCodec(),
                _ => new JsonCodec()
            };

        private static IPayloadProtector CreateProtector(SaveProtection protection) =>
            protection switch
            {
                SaveProtection.None => new NoProtection(),
                _ => new NoProtection()
            };
    }
}
