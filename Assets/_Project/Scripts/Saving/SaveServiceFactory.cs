namespace Company.ChestGame.Saving
{
    // Turns a profile - or a bare (storage, codec, protection) triple - into a plain, undecorated
    // SaveService, by handing each enum to SaveComponentFactory and assembling the three results.
    // Static and stateless, like PoolFactory and CatalogBuilder.
    //
    // The ceiling, stated as a rule rather than left to be inferred from what this type happens not
    // to take yet: this will never grow a parameter for a SaveMigrator, an ILegacyImport, a
    // ThreadHoppingStore, or a SaveScheduler<T>. Which store a scheduler is safe to call
    // FlushBlocking on, whether this game even has a legacy save to import, whether the frame cost
    // of encoding is worth trading for a worker-thread hop - none of that is answerable from a
    // profile's three dropdowns, and every one of them is a composition-root decision, not a
    // factory's. Folding them in here would regrow the five-job factory this phase split apart, one
    // "just this one extra parameter" at a time. A caller that needs any of them composes a
    // SaveService by hand from SaveComponentFactory.CreateStore/CreateCodec/CreateProtector instead
    // of through this type - the same boundary PoolFactory.Create draws against whatever assembles
    // the screen a pool actually lives in: PoolFactory does not know a screen exists, and after this
    // phase, this type does not know a scheduler does. See docs/saving.md.
    public static class SaveServiceFactory
    {
        public static ISaveService Create(SaveProfileSO profile, SaveFactoryInputs inputs = null)
        {
            // Unity-null, not C#-null: a destroyed SaveProfileSO must fail here too.
            if (profile == null) throw SaveException.NoProfile();

            return CreateFrom(profile.Storage, profile.Codec, profile.Protection, inputs);
        }

        // inputs defaults to SaveFactoryInputs.Defaults() so a caller that does not care about the
        // file root, the PlayerPrefs prefix, or any protector's key material keeps getting exactly
        // what it always got from this method.
        public static ISaveService CreateFrom(SaveStorage storage, SaveCodec codec, SaveProtection protection, SaveFactoryInputs inputs = null)
        {
            inputs ??= SaveFactoryInputs.Defaults();

            return new SaveService(
                SaveComponentFactory.CreateCodec(codec),
                SaveComponentFactory.CreateProtector(protection, inputs),
                SaveComponentFactory.CreateStore(storage, inputs));
        }
    }
}
