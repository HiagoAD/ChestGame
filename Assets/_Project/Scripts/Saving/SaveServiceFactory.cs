namespace Company.ChestGame.Saving
{
    // Turns a profile - or a bare (storage, codec, protection) triple - into a plain, undecorated
    // SaveService. Static and stateless.
    //
    // The ceiling is stated as a rule rather than left to be inferred from what this happens not to
    // take yet: it will never grow a parameter for anything beyond what a profile's three dropdowns
    // already answer. A caller that needs more - a migration chain, a legacy import, a decorator, a
    // scheduler - composes a SaveService from its components directly instead of asking this
    // factory to grow a fifth parameter. See docs/saving.md for why this ceiling is permanent
    // rather than provisional.
    public static class SaveServiceFactory
    {
        public static ISaveService Create(SaveProfileSO profile, SaveFactoryInputs inputs = null)
        {
            // Unity-null, not C#-null: a destroyed SaveProfileSO must fail here too.
            if (profile == null) throw SaveException.NoProfile();

            return CreateFrom(profile.Storage, profile.Codec, profile.Protection, inputs);
        }

        // inputs defaults so a caller that cares about none of the file root, the PlayerPrefs
        // prefix or a protector's key material need not name any of them.
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
