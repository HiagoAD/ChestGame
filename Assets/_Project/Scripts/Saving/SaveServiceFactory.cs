namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Turns a profile, or a bare (storage, codec, protection) triple, into a plain, undecorated
    /// <see cref="SaveService"/>. Static and stateless.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "SaveComponentFactory, SaveFactoryInputs and SaveServiceFactory".
    /// </remarks>
    public static class SaveServiceFactory
    {
        /// <exception cref="SaveException">
        /// When <paramref name="profile"/> is null or destroyed, or when <paramref name="inputs"/>
        /// supplies no key material for the protector <paramref name="profile"/> selects.
        /// </exception>
        /// <remarks>
        /// See docs/saving.md, "SaveComponentFactory, SaveFactoryInputs and SaveServiceFactory".
        /// </remarks>
        public static ISaveService Create(SaveProfileSO profile, SaveFactoryInputs inputs = null)
        {
            if (profile == null) throw SaveException.NoProfile();

            return CreateFrom(profile.Storage, profile.Codec, profile.Protection, inputs);
        }

        /// <exception cref="SaveException">
        /// When <paramref name="inputs"/> supplies no key material for the protector
        /// <paramref name="protection"/> names.
        /// </exception>
        /// <remarks>
        /// See docs/saving.md, "SaveComponentFactory, SaveFactoryInputs and SaveServiceFactory".
        /// </remarks>
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
